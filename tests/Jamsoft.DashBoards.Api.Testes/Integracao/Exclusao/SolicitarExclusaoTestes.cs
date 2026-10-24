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
            Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);

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
}
