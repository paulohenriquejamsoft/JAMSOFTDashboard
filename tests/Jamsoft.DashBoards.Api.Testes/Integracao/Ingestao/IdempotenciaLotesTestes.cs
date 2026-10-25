using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Ingestao;

/// <summary>ING-05: lote repetido (mesmo <c>batchId</c> no tenant) responde igual ao original sem regravar (AD-014).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class IdempotenciaLotesTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/coletor/vendas/lotes";

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

    private static VendaContrato NovaVenda(int vendaId, string situacao = "Finalizada") => new(
        VendaId: vendaId, TipoDfe: "NFC-e", DataHora: new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.FromHours(-3)),
        Valor: 10m, CodCliente: null, NomeCliente: null, VendedorCodigo: null, VendedorNome: null,
        Situacao: situacao, NomeEmpresa: "Loja", CodEmpresa: 1, QtdItens: 1);

    private static LoteVendas NovoLote(Tenant tenant, Guid batchId, IReadOnlyList<VendaContrato> vendas) =>
        new(batchId, tenant.IdPublico, Guid.NewGuid(), "Vendas", null, vendas);

    private async Task<(Tenant Tenant, string Token)> AutorizadoAsync(string nome = "Cliente Idempotência")
    {
        var tenant = await Dados.NovoTenantAsync(banco, nome);
        var (coletor, _) = await Dados.NovoColetorComSegredoAsync(banco, tenant);
        return (tenant, Acesso.TokenColetor(coletor, [tenant.IdPublico]));
    }

    private async Task<(HttpStatusCode Status, string Corpo)> EnviarAsync(string token, LoteVendas lote)
    {
        using var resposta = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token, lote));
        return (resposta.StatusCode, await resposta.Content.ReadAsStringAsync());
    }

    private async Task<(int Vendas, int Lotes, int Canceladas)> ContagensAsync(Tenant tenant)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        return (await contexto.Vendas.CountAsync(), await contexto.LotesIngestao.CountAsync(),
            await contexto.Vendas.CountAsync(v => v.Situacao == "Cancelada"));
    }

    [Fact]
    public async Task Segundo_envio_do_mesmo_batchId_responde_200_com_o_corpo_original_e_nao_regrava()
    {
        var (tenant, token) = await AutorizadoAsync();
        var batchId = Guid.NewGuid();
        var lote = NovoLote(tenant, batchId, [NovaVenda(1), NovaVenda(2), NovaVenda(3) with { Situacao = "Aberta" }]);
        var primeiro = await EnviarAsync(token, lote);
        var antes = await DadosTenant.ImpressaoAsync(banco, tenant.Id);

        // Mesmo lote reenviado, agora com conteúdo diferente: nada é regravado.
        var segundo = await EnviarAsync(token, lote with
        {
            Vendas = [NovaVenda(1, "Cancelada"), NovaVenda(2, "Cancelada"), NovaVenda(4)],
        });

        Assert.Equal(HttpStatusCode.OK, primeiro.Status);
        Assert.Equal(HttpStatusCode.OK, segundo.Status);
        Assert.Equal(primeiro.Corpo, segundo.Corpo);
        Assert.Contains("\"inseridos\":2", primeiro.Corpo);
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, tenant.Id));
        Assert.Equal((2, 1, 0), await ContagensAsync(tenant));
    }

    [Fact]
    public async Task Dois_envios_simultaneos_do_mesmo_lote_dao_a_mesma_resposta_e_uma_linha_em_LOTE_INGESTAO()
    {
        var (tenant, token) = await AutorizadoAsync();
        var lote = NovoLote(tenant, Guid.NewGuid(), Enumerable.Range(1, 200).Select(i => NovaVenda(i)).ToList());

        var respostas = await Task.WhenAll(
            Task.Run(() => EnviarAsync(token, lote)),
            Task.Run(() => EnviarAsync(token, lote)));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(respostas[0].Corpo, respostas[1].Corpo);
        Assert.Contains("\"inseridos\":200", respostas[0].Corpo);
        Assert.Equal((200, 1, 0), await ContagensAsync(tenant));
    }

    [Fact]
    public async Task Mesmo_batchId_em_outro_tenant_e_processado_de_forma_independente()
    {
        var (a, tokenA) = await AutorizadoAsync("Cliente Idempotência A");
        var (b, tokenB) = await AutorizadoAsync("Cliente Idempotência B");
        var batchId = Guid.NewGuid();
        await EnviarAsync(tokenA, NovoLote(a, batchId, [NovaVenda(1)]));

        var emB = await EnviarAsync(tokenB, NovoLote(b, batchId, [NovaVenda(1), NovaVenda(2)]));

        Assert.Equal(HttpStatusCode.OK, emB.Status);
        Assert.Contains("\"inseridos\":2", emB.Corpo);
        Assert.Equal((1, 1, 0), await ContagensAsync(a));
        Assert.Equal((2, 1, 0), await ContagensAsync(b));
    }
}
