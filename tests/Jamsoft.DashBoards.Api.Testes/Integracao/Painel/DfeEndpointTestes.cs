using System.Net;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Painel;

/// <summary><c>GET /api/painel/dfe</c> (DFE-01, DFE-02).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class DfeEndpointTestes(BancoDeTestes banco) : IAsyncLifetime
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

    private sealed record Tipo(string Nome, decimal Faturamento, int Vendas, decimal Participacao);

    private async Task<List<Tipo>> DfeAsync(ConjuntoVendas c, string extra = "")
    {
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, c.Tenant);
        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get,
            $"/api/painel/dfe?inicio=2026-10-11&fim=2026-10-20{extra}", Acesso.Token(usuario, c.Tenant)));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await Sessao.JsonAsync(resposta)).GetProperty("tipos").EnumerateArray()
            .Select(t => new Tipo(t.GetProperty("tipo").GetString()!, t.GetProperty("faturamento").GetDecimal(),
                t.GetProperty("vendas").GetInt32(), t.GetProperty("participacao").GetDecimal()))
            .ToList();
    }

    [Fact]
    public async Task Tres_NFe_e_sete_NFCe_dividem_faturamento_vendas_e_participacao()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e"));
        var vendas = Enumerable.Range(1, 10)
            .Select(i => VendasSemeadas.Venda(i % 2 == 0 ? c.Matriz : c.Filial, i, VendasSemeadas.Sp(10, 12, 10, i), 100.00m,
                i == 10 ? "Contingência" : "Finalizada", i <= 3 ? "NF-e" : "NFC-e"))
            // Cancelada e Inutilizada não entram.
            .Append(VendasSemeadas.Venda(c.Matriz, 11, VendasSemeadas.Sp(10, 13, 10, 0), 500.00m, "Cancelada", "NF-e"))
            .Append(VendasSemeadas.Venda(c.Matriz, 12, VendasSemeadas.Sp(10, 13, 11, 0), 500.00m, "Inutilizada", "NFC-e"))
            .ToArray();
        await VendasSemeadas.InserirAsync(banco, c, vendas);

        var tipos = await DfeAsync(c);

        Assert.Equal([new Tipo("NF-e", 300.00m, 3, 30.00m), new Tipo("NFC-e", 700.00m, 7, 70.00m)], tipos);
    }

    [Fact]
    public async Task Periodo_so_com_NFCe_traz_NFe_com_zeros()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e só NFC-e"));
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 1, VendasSemeadas.Sp(10, 12, 10, 0), 42.10m, tipoDfe: "NFC-e"),
            VendasSemeadas.Venda(c.Filial, 2, VendasSemeadas.Sp(10, 14, 10, 0), 7.90m, tipoDfe: "NFC-e"));

        var tipos = await DfeAsync(c);

        Assert.Equal([new Tipo("NF-e", 0m, 0, 0m), new Tipo("NFC-e", 50.00m, 2, 100.00m)], tipos);
    }

    [Fact]
    public async Task Filtro_com_uma_empresa_restringe_o_DFe_as_vendas_dela()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e Filial"));

        var tipos = await DfeAsync(c, $"&empresas={c.Filial.IdPublico}");

        // Filial no período: 5 (NF-e, 200) e 6 (NFC-e, 25,50); total 225,50.
        Assert.Equal([new Tipo("NF-e", 200.00m, 1, 88.69m), new Tipo("NFC-e", 25.50m, 1, 11.31m)], tipos);
    }
}
