using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Exclusao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ExclusoesEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private ApiFactory _api = null!;
    private HttpClient _cliente = null!;
    private ServiceProvider _servicos = null!;
    private string _token = "";

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco);
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _token = await Acesso.TokenSuperAdminAsync(banco);
        _servicos = ProcessadorExclusaoTestes.ServicosProcessador(banco);
        await ProcessadorExclusaoTestes.ProcessarTudoAsync(Processador);
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
        await _servicos.DisposeAsync();
    }

    private ProcessadorExclusao Processador => _servicos.GetRequiredService<ProcessadorExclusao>();
    private PlanoExclusao Plano => _servicos.GetRequiredService<PlanoExclusao>();

    private Task<HttpResponseMessage> ObterAsync(Guid jobId, string? token = null) =>
        _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, $"/api/admin/exclusoes/{jobId}", token ?? _token));

    private Task<HttpResponseMessage> RetomarAsync(Guid jobId, string? token = null) =>
        _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Post, $"/api/admin/exclusoes/{jobId}/retomar", token ?? _token));

    private async Task<ExclusaoTenant> JobAsync(int id)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.ExclusoesTenant.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    /// <summary>Job que falhou na 2ª etapa, depois de apagar 4 linhas da 1ª (que já não tem linhas do tenant).</summary>
    private async Task<ExclusaoTenant> JobFalhouAsync(string nome)
    {
        var tenant = await Dados.NovoTenantAsync(banco, nome);
        await DadosTenant.SemearAsync(banco, tenant.Id, empresas: 0, usuarios: 3);
        var job = await ProcessadorExclusaoTestes.NovoJobAsync(banco, tenant, Guid.NewGuid(), SituacaoExclusao.Falhou,
            etapa: Plano.Etapas[1].Tabela, linhas: $"{{\"{Plano.Etapas[0].Tabela}\":4}}");
        await banco.ExecutarAsync($"UPDATE EXCLUSAO_TENANT SET ERRO = 'Etapa {Plano.Etapas[1].Tabela}: falha' WHERE ID = {job.Id}");
        return await JobAsync(job.Id);
    }

    [Fact]
    public async Task Consulta_job_existente_com_situacao_etapa_linhas_erro_e_datas()
    {
        var job = await JobFalhouAsync("Cliente Consulta Exclusão");

        using var resposta = await ObterAsync(job.IdPublico);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        Assert.Equal(job.IdPublico, json.GetProperty("jobId").GetGuid());
        Assert.Equal(job.TenantIdPublico, json.GetProperty("tenantIdPublico").GetGuid());
        Assert.Equal("Cliente Consulta Exclusão", json.GetProperty("tenantNome").GetString());
        Assert.Equal("Falhou", json.GetProperty("situacao").GetString());
        Assert.Equal(Plano.Etapas[1].Tabela, json.GetProperty("etapaAtual").GetString());
        Assert.Equal(4, json.GetProperty("linhasRemovidas").GetProperty(Plano.Etapas[0].Tabela).GetInt64());
        Assert.Equal($"Etapa {Plano.Etapas[1].Tabela}: falha", json.GetProperty("erro").GetString());
        Assert.Equal(DateTime.SpecifyKind(job.CriadoEm, DateTimeKind.Utc), json.GetProperty("criadoEm").GetDateTime());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("concluidoEm").ValueKind);
    }

    [Fact]
    public async Task Job_inexistente_responde_404_na_consulta_e_na_retomada()
    {
        using var consulta = await ObterAsync(Guid.NewGuid());
        using var retomada = await RetomarAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, consulta.StatusCode);
        Assert.Equal("NAO_ENCONTRADO", await Sessao.CodigoAsync(consulta));
        Assert.Equal(HttpStatusCode.NotFound, retomada.StatusCode);
        Assert.Equal("NAO_ENCONTRADO", await Sessao.CodigoAsync(retomada));
    }

    [Fact]
    public async Task Retomar_job_Falhou_responde_202_e_o_worker_conclui_a_partir_da_etapa_salva()
    {
        var job = await JobFalhouAsync("Cliente Retomada");
        var primeira = Plano.Etapas[0].Tabela;
        // Conta os DELETEs na 1ª tabela: a retomada não pode voltar a ela.
        await banco.ExecutarAsync($"""
            CREATE TABLE TESTE_DELETES_PRIMEIRA_ETAPA (ID INT IDENTITY PRIMARY KEY);
            EXEC('CREATE TRIGGER [TR_TESTE_DELETES_PRIMEIRA_ETAPA] ON [{primeira}] AFTER DELETE AS
                  INSERT INTO TESTE_DELETES_PRIMEIRA_ETAPA DEFAULT VALUES;');
            """);
        try
        {
            using var resposta = await RetomarAsync(job.IdPublico);

            Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
            Assert.Equal(job.IdPublico, (await Sessao.JsonAsync(resposta)).GetProperty("jobId").GetGuid());
            var pendente = await JobAsync(job.Id);
            Assert.Equal(SituacaoExclusao.Pendente, pendente.Situacao);
            Assert.Equal(Plano.Etapas[1].Tabela, pendente.EtapaAtual);

            Assert.True(await Processador.ProcessarProximoAsync());

            Assert.Equal(0, await banco.ConsultarEscalarAsync<int>("SELECT COUNT(*) FROM TESTE_DELETES_PRIMEIRA_ETAPA"));
        }
        finally
        {
            await banco.ExecutarAsync(
                "DROP TRIGGER [TR_TESTE_DELETES_PRIMEIRA_ETAPA]; DROP TABLE TESTE_DELETES_PRIMEIRA_ETAPA;");
        }

        var concluido = await JobAsync(job.Id);
        Assert.Equal(SituacaoExclusao.Concluida, concluido.Situacao);
        Assert.Equal(
            new Dictionary<string, long> { [primeira] = 4, ["REFRESH_TOKEN"] = 3, ["USUARIO"] = 3, ["TENANT"] = 1 },
            JsonSerializer.Deserialize<Dictionary<string, long>>(concluido.LinhasRemovidas!));
        Assert.All(await DadosTenant.LinhasAsync(banco, job.TenantId), p => Assert.Equal(0, p.Value));

        await using var contexto = banco.CriarContexto();
        var registro = await contexto.Auditoria.AsNoTracking()
            .SingleAsync(a => a.RecursoIdPublico == job.IdPublico && a.Operacao == "EXCLUSAO_RETOMADA");
        Assert.Equal("SuperAdmin", registro.Perfil);
        Assert.Equal(job.TenantIdPublico, registro.TenantIdPublico);
        Assert.Equal("Cliente Retomada", registro.TenantNome);
        Assert.Contains("\"situacao\":\"Pendente\"", registro.Detalhes);
    }

    [Fact]
    public async Task Retomar_job_Concluida_responde_409_EXCLUSAO_NAO_RETOMAVEL()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Já Excluído");
        var job = await ProcessadorExclusaoTestes.NovoJobAsync(banco, tenant, Guid.NewGuid());
        Assert.True(await Processador.ProcessarProximoAsync());

        using var resposta = await RetomarAsync(job.IdPublico);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(ExclusoesEndpoints.CodigoNaoRetomavel, await Sessao.CodigoAsync(resposta));
        Assert.Equal(SituacaoExclusao.Concluida, (await JobAsync(job.Id)).Situacao);
    }

    [Fact]
    public async Task Admin_recebe_403_na_consulta_e_na_retomada()
    {
        var job = await JobFalhouAsync("Cliente Exclusão Admin 403");
        var tenantAdmin = await Dados.NovoTenantAsync(banco, "Cliente do Admin");
        var token = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenantAdmin), tenantAdmin);

        using var consulta = await ObterAsync(job.IdPublico, token);
        using var retomada = await RetomarAsync(job.IdPublico, token);

        Assert.Equal(HttpStatusCode.Forbidden, consulta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(consulta));
        Assert.Equal(HttpStatusCode.Forbidden, retomada.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(retomada));
        Assert.Equal(SituacaoExclusao.Falhou, (await JobAsync(job.Id)).Situacao);
    }
}
