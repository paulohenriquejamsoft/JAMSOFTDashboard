using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Ingestao;

/// <summary><c>POST /api/coletor/execucoes</c>: ING-09.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ExecucoesEndpointTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/coletor/execucoes";

    private static readonly DateTimeOffset Inicio = new(2026, 10, 22, 8, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Fim = new(2026, 10, 22, 8, 4, 30, TimeSpan.FromHours(-3));

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

    private async Task<(Tenant Tenant, string Token)> AutorizadoAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Execuções");
        var (coletor, _) = await Dados.NovoColetorComSegredoAsync(banco, tenant);
        return (tenant, Acesso.TokenColetor(coletor, [tenant.IdPublico]));
    }

    private static RelatorioExecucao Relatorio(Tenant tenant, Guid configuracao, string situacao, DateTimeOffset inicio,
        DateTimeOffset fim, int registros = 0, string? mensagem = null) =>
        new(tenant.IdPublico, configuracao, "Vendas", "SRV01/SGE_LOJA", inicio, fim, situacao, registros, mensagem);

    private async Task<HttpResponseMessage> EnviarAsync(string token, RelatorioExecucao relatorio) =>
        await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token, relatorio));

    private async Task<OrigemSincronizacao> OrigemAsync(Tenant tenant, Guid configuracao)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        return await contexto.OrigensSincronizacao.AsNoTracking().SingleAsync(o => o.ConfiguracaoId == configuracao);
    }

    [Fact]
    public async Task Sucesso_grava_a_execucao_e_ULTIMO_SUCESSO_EM()
    {
        var (tenant, token) = await AutorizadoAsync();
        var configuracao = Guid.NewGuid();

        using var resposta = await EnviarAsync(token, Relatorio(tenant, configuracao, "Sucesso", Inicio, Fim, registros: 1200));

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        var origem = await OrigemAsync(tenant, configuracao);
        Assert.Equal(("Vendas", "SRV01/SGE_LOJA", Inicio.UtcDateTime, Fim.UtcDateTime, "Sucesso", 1200, (string?)null, Fim.UtcDateTime),
            (origem.Rotina, origem.BancoOrigem, origem.UltimaExecucaoInicio, origem.UltimaExecucaoFim, origem.UltimaSituacao,
                origem.RegistrosUltima, origem.UltimaMensagem, origem.UltimoSucessoEm));
    }

    [Fact]
    public async Task Falha_grava_situacao_e_mensagem_e_preserva_o_ultimo_sucesso_anterior()
    {
        var (tenant, token) = await AutorizadoAsync();
        var configuracao = Guid.NewGuid();
        using (var sucesso = await EnviarAsync(token, Relatorio(tenant, configuracao, "Sucesso", Inicio, Fim, registros: 10)))
            Assert.Equal(HttpStatusCode.NoContent, sucesso.StatusCode);
        var inicioFalha = Inicio.AddMinutes(15);
        var fimFalha = Fim.AddMinutes(15);

        using var resposta = await EnviarAsync(token,
            Relatorio(tenant, configuracao, "Falha", inicioFalha, fimFalha, registros: 3, mensagem: "Conexão: servidor inacessível"));

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        var origem = await OrigemAsync(tenant, configuracao);
        Assert.Equal((inicioFalha.UtcDateTime, fimFalha.UtcDateTime, "Falha", 3, "Conexão: servidor inacessível", Fim.UtcDateTime),
            (origem.UltimaExecucaoInicio, origem.UltimaExecucaoFim, origem.UltimaSituacao, origem.RegistrosUltima,
                origem.UltimaMensagem, origem.UltimoSucessoEm));
    }

    [Fact]
    public async Task Tenant_fora_do_token_responde_403_TENANT_NAO_AUTORIZADO_sem_gravar()
    {
        var (_, token) = await AutorizadoAsync();
        var outro = await Dados.NovoTenantAsync(banco, "Cliente não autorizado");
        var antes = await DadosTenant.ImpressaoAsync(banco, outro.Id);

        using var resposta = await EnviarAsync(token, Relatorio(outro, Guid.NewGuid(), "Sucesso", Inicio, Fim));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("TENANT_NAO_AUTORIZADO", await Sessao.CodigoAsync(resposta));
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, outro.Id));
    }

    [Fact]
    public async Task Mensagem_longa_e_truncada_em_500_caracteres()
    {
        var (tenant, token) = await AutorizadoAsync();
        var configuracao = Guid.NewGuid();
        var mensagem = string.Concat(Enumerable.Repeat("0123456789", 60));

        using var resposta = await EnviarAsync(token, Relatorio(tenant, configuracao, "Falha", Inicio, Fim, mensagem: mensagem));

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(mensagem[..500], (await OrigemAsync(tenant, configuracao)).UltimaMensagem);
    }
}
