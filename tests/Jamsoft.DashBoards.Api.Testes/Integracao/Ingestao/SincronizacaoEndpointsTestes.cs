using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Ingestao;

/// <summary><c>GET /api/sincronizacao</c> (STS-01) e <c>GET /api/sincronizacao/resumo</c> (STS-02).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class SincronizacaoEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
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

    /// <summary>Grava uma origem do tenant com a última execução informada.</summary>
    private async Task<OrigemSincronizacao> OrigemAsync(Tenant tenant, string rotina, string bancoOrigem, DateTime? ultimoSucesso,
        string? situacao = null, string? mensagem = null)
    {
        var coletor = await Dados.NovoColetorAsync(banco);
        await using var contexto = banco.CriarContexto();
        var origem = new OrigemSincronizacao
        {
            TenantId = tenant.Id,
            ColetorId = coletor.Id,
            ConfiguracaoId = Guid.NewGuid(),
            Rotina = rotina,
            BancoOrigem = bancoOrigem,
            UltimaExecucaoInicio = new DateTime(2026, 10, 24, 11, 0, 0, DateTimeKind.Utc),
            UltimaExecucaoFim = new DateTime(2026, 10, 24, 11, 5, 0, DateTimeKind.Utc),
            UltimaSituacao = situacao,
            UltimaMensagem = mensagem,
            RegistrosUltima = 42,
            UltimoSucessoEm = ultimoSucesso,
        };
        contexto.OrigensSincronizacao.Add(origem);
        await contexto.SaveChangesAsync();
        return origem;
    }

    private async Task<HttpResponseMessage> GetAsync(string rota, string token) =>
        await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, rota, token));

    [Fact]
    public async Task Admin_lista_as_origens_do_proprio_tenant_com_os_campos_do_STS_01()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Status");
        var outro = await Dados.NovoTenantAsync(banco, "Outro Cliente Status");
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        var sucesso = new DateTime(2026, 10, 23, 18, 30, 0, DateTimeKind.Utc);
        var origem = await OrigemAsync(tenant, "Vendas", "SRV01/LOJA", sucesso, "Falha", "Conexão: servidor inacessível");
        await OrigemAsync(outro, "Vendas", "SRV09/OUTRO", sucesso, "Sucesso");

        using var resposta = await GetAsync("/api/sincronizacao", Acesso.Token(admin, tenant));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var item = Assert.Single((await Sessao.JsonAsync(resposta)).EnumerateArray());
        Assert.Equal(origem.IdPublico, item.GetProperty("idPublico").GetGuid());
        Assert.Equal("Vendas", item.GetProperty("rotina").GetString());
        Assert.Equal("SRV01/LOJA", item.GetProperty("bancoOrigem").GetString());
        Assert.Equal(new DateTimeOffset(sucesso), item.GetProperty("ultimoSucessoEm").GetDateTimeOffset());
        Assert.Equal("Falha", item.GetProperty("ultimaSituacao").GetString());
        Assert.Equal("Conexão: servidor inacessível", item.GetProperty("ultimaMensagem").GetString());
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 11, 0, 0, TimeSpan.Zero), item.GetProperty("ultimaExecucaoInicio").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 11, 5, 0, TimeSpan.Zero), item.GetProperty("ultimaExecucaoFim").GetDateTimeOffset());
        Assert.Equal(42, item.GetProperty("registrosUltima").GetInt32());
    }

    [Fact]
    public async Task Consulta_na_lista_responde_403()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var consulta = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);

        using var resposta = await GetAsync("/api/sincronizacao", Acesso.Token(consulta, tenant));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
    }

    [Fact]
    public async Task Resumo_para_Consulta_traz_o_menor_ULTIMO_SUCESSO_EM_das_origens_do_tenant()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Resumo");
        var outro = await Dados.NovoTenantAsync(banco, "Outro Cliente Resumo");
        var consulta = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);
        var menor = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        await OrigemAsync(tenant, "Vendas", "SRV01/LOJA1", menor.AddDays(2));
        await OrigemAsync(tenant, "Vendas", "SRV01/LOJA2", menor);
        // Origem que nunca teve sucesso não entra no mínimo; a de outro tenant também não.
        await OrigemAsync(tenant, "Vendas", "SRV01/LOJA3", null, "Falha");
        await OrigemAsync(outro, "Vendas", "SRV09/OUTRO", menor.AddDays(-10));

        using var resposta = await GetAsync("/api/sincronizacao/resumo", Acesso.Token(consulta, tenant));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(new DateTimeOffset(menor), (await Sessao.JsonAsync(resposta)).GetProperty("ultimoSucessoEm").GetDateTimeOffset());
    }

    [Fact]
    public async Task Resumo_sem_sincronizacao_traz_ultimoSucessoEm_nulo()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente sem sincronização");
        var consulta = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);

        using var resposta = await GetAsync("/api/sincronizacao/resumo", Acesso.Token(consulta, tenant));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            (await Sessao.JsonAsync(resposta)).GetProperty("ultimoSucessoEm").ValueKind);
    }
}
