using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Exclusao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class SolicitarExclusaoTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private ApiFactory _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco);
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private Task<HttpResponseMessage> SolicitarAsync(Tenant tenant, string? token, string? confirmacaoNome) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, $"/api/admin/tenants/{tenant.IdPublico}/exclusao", token,
            new { confirmacaoNome }));

    private async Task<(bool Ativo, List<ExclusaoTenant> Jobs)> EstadoAsync(Tenant tenant)
    {
        await using var contexto = banco.CriarContexto();
        var ativo = await contexto.Tenants.Where(t => t.Id == tenant.Id).Select(t => t.Ativo).SingleAsync();
        var jobs = await contexto.ExclusoesTenant.AsNoTracking().Where(j => j.TenantId == tenant.Id).ToListAsync();
        return (ativo, jobs);
    }

    [Fact]
    public async Task Admin_recebe_403_e_nada_e_criado()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Admin");
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);

        using var resposta = await SolicitarAsync(tenant, Acesso.Token(admin, tenant), tenant.Nome);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
        var (ativo, jobs) = await EstadoAsync(tenant);
        Assert.True(ativo);
        Assert.Empty(jobs);
    }

    [Theory]
    [InlineData("cliente exclusão caixa")]   // caixa diferente
    [InlineData("Cliente Exclusão Caixa ")]  // espaço extra
    public async Task Nome_diferente_responde_422_CONFIRMACAO_INVALIDA_e_nada_e_criado(string digitado)
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Caixa");

        using var resposta = await SolicitarAsync(tenant, await Acesso.TokenSuperAdminAsync(banco), digitado);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal(SolicitarExclusaoEndpoint.CodigoConfirmacaoInvalida, await Sessao.CodigoAsync(resposta));
        var (ativo, jobs) = await EstadoAsync(tenant);
        Assert.True(ativo);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task Confirmacao_valida_responde_202_inativa_tenant_cria_job_Pendente_e_audita()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Válida");
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        var tokenAdmin = Acesso.Token(admin, tenant);
        // Situação ativa fica no cache antes da solicitação.
        using (var antes = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/auth/eu", tokenAdmin)))
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);
        var superAdmin = await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin);

        using var resposta = await SolicitarAsync(tenant, Acesso.Token(superAdmin, null), "Cliente Exclusão Válida");

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        var jobId = (await Sessao.JsonAsync(resposta)).GetProperty("jobId").GetGuid();
        var (ativo, jobs) = await EstadoAsync(tenant);
        Assert.False(ativo);
        var job = Assert.Single(jobs);
        Assert.Equal(jobId, job.IdPublico);
        Assert.Equal(SituacaoExclusao.Pendente, job.Situacao);
        Assert.Equal(tenant.IdPublico, job.TenantIdPublico);
        Assert.Equal("Cliente Exclusão Válida", job.TenantNome);
        Assert.Equal(superAdmin.IdPublico, job.SolicitadoPorIdPublico);
        // Inativação imediata: o cache do tenant foi invalidado.
        using (var depois = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/auth/eu", tokenAdmin)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, depois.StatusCode);
            Assert.Equal("ACESSO_DESATIVADO", await Sessao.CodigoAsync(depois));
        }

        await using var contexto = banco.CriarContexto();
        var registro = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == jobId);
        Assert.Equal("EXCLUSAO_SOLICITADA", registro.Operacao);
        Assert.Equal(superAdmin.IdPublico, registro.UsuarioIdPublico);
        Assert.Equal(tenant.IdPublico, registro.TenantIdPublico);
        Assert.Equal("Cliente Exclusão Válida", registro.TenantNome);
        Assert.Equal("Sucesso", registro.Resultado);
        Assert.Contains("\"situacao\":\"Pendente\"", registro.Detalhes);
    }

    [Fact]
    public async Task Segunda_solicitacao_com_job_ativo_responde_409_EXCLUSAO_EM_ANDAMENTO()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Dupla");
        var token = await Acesso.TokenSuperAdminAsync(banco);
        using (var primeira = await SolicitarAsync(tenant, token, tenant.Nome))
            Assert.Equal(HttpStatusCode.Accepted, primeira.StatusCode);

        using var segunda = await SolicitarAsync(tenant, token, tenant.Nome);

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal(SolicitarExclusaoEndpoint.CodigoExclusaoEmAndamento, await Sessao.CodigoAsync(segunda));
        var (_, jobs) = await EstadoAsync(tenant);
        Assert.Single(jobs);
    }

    [Fact]
    public async Task Resposta_409_EXCLUSAO_EM_ANDAMENTO_traz_o_jobId_do_job_ativo()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Job Ativo");
        var token = await Acesso.TokenSuperAdminAsync(banco);
        Guid jobId;
        using (var primeira = await SolicitarAsync(tenant, token, tenant.Nome))
            jobId = (await Sessao.JsonAsync(primeira)).GetProperty("jobId").GetGuid();

        using var segunda = await SolicitarAsync(tenant, token, tenant.Nome);

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        var corpo = await Sessao.JsonAsync(segunda);
        Assert.Equal(SolicitarExclusaoEndpoint.CodigoExclusaoEmAndamento, corpo.GetProperty("codigo").GetString());
        Assert.Equal(jobId, corpo.GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task Solicitacao_com_job_EmExecucao_responde_409_EXCLUSAO_EM_ANDAMENTO_sem_criar_job()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Em Execução");
        var emExecucao = new ExclusaoTenant
        {
            TenantId = tenant.Id,
            TenantIdPublico = tenant.IdPublico,
            TenantNome = tenant.Nome,
            Situacao = SituacaoExclusao.EmExecucao,
            SolicitadoPorIdPublico = Guid.NewGuid(),
            IniciadoEm = DateTime.UtcNow,
            // Heartbeat recente: nenhum processador de outro teste o considera abandonado.
            HeartbeatEm = DateTime.UtcNow,
        };
        await using (var contexto = banco.CriarContexto())
        {
            contexto.ExclusoesTenant.Add(emExecucao);
            await contexto.SaveChangesAsync();
        }

        try
        {
            using var resposta = await SolicitarAsync(tenant, await Acesso.TokenSuperAdminAsync(banco), tenant.Nome);

            Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
            var corpo = await Sessao.JsonAsync(resposta);
            Assert.Equal(SolicitarExclusaoEndpoint.CodigoExclusaoEmAndamento, corpo.GetProperty("codigo").GetString());
            Assert.Equal(emExecucao.IdPublico, corpo.GetProperty("jobId").GetGuid());
            var (_, jobs) = await EstadoAsync(tenant);
            Assert.Equal(emExecucao.Id, Assert.Single(jobs).Id);
        }
        finally
        {
            // Encerra o job semeado para não ficar ativo no banco compartilhado da coleção.
            await using var limpeza = banco.CriarContexto();
            await limpeza.ExclusoesTenant.Where(j => j.Id == emExecucao.Id)
                .ExecuteUpdateAsync(j => j.SetProperty(x => x.Situacao, SituacaoExclusao.Concluida));
        }
    }
}
