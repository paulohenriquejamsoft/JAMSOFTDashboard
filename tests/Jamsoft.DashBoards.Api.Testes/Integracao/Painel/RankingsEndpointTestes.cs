using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Painel;

/// <summary><c>GET /api/painel/rankings</c> (RNK-01, RNK-02, RNK-03, RNK-04).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class RankingsEndpointTestes(BancoDeTestes banco) : IAsyncLifetime
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

    private sealed record Item(string Nome, decimal Faturamento, int Vendas, decimal Participacao);

    private static List<Item> Lista(JsonElement corpo, string nome) =>
        corpo.GetProperty(nome).EnumerateArray().Select(i => new Item(i.GetProperty("nome").GetString()!,
            i.GetProperty("faturamento").GetDecimal(), i.GetProperty("vendas").GetInt32(),
            i.GetProperty("participacao").GetDecimal())).ToList();

    private async Task<JsonElement> RankingsAsync(ConjuntoVendas c, string periodo = "inicio=2026-10-11&fim=2026-10-20")
    {
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, c.Tenant);
        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, $"/api/painel/rankings?{periodo}",
            Acesso.Token(usuario, c.Tenant)));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return await Sessao.JsonAsync(resposta);
    }

    /// <summary>Vendedores 1..12, cada um com uma venda de 10 × código (total 780).</summary>
    private async Task<ConjuntoVendas> DozeVendedoresAsync()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Rankings 12"));
        await VendasSemeadas.InserirAsync(banco, c, Enumerable.Range(1, 12)
            .Select(k => VendasSemeadas.Venda(c.Matriz, k, VendasSemeadas.Sp(10, 12, 10, 0), 10m * k,
                vendedorCodigo: k, vendedorNome: $"Vendedor {k:00}"))
            .ToArray());
        return c;
    }

    [Fact]
    public async Task Doze_vendedores_retornam_os_10_de_maior_faturamento_em_ordem_decrescente()
    {
        var corpo = await RankingsAsync(await DozeVendedoresAsync());

        var vendedores = Lista(corpo, "vendedores");
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(k => $"Vendedor {k:00}"), vendedores.Select(v => v.Nome));
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(k => 10m * k), vendedores.Select(v => v.Faturamento));
    }

    [Fact]
    public async Task Itens_trazem_nome_faturamento_vendas_e_participacao_exatos()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Rankings Campos"));

        var corpo = await RankingsAsync(c);

        // Efetivadas: 1 (Ana, X, Matriz, 100), 2 (Bruno, -, Matriz, 50), 5 (Ana, Y, Filial, 200), 6 (-, Y, Filial, 25,50).
        Assert.Equal(375.50m, corpo.GetProperty("totalFaturamento").GetDecimal());
        Assert.Equal(
            [new Item("Ana", 300.00m, 2, 79.89m), new Item("Bruno", 50.00m, 1, 13.32m), new Item("Não informado", 25.50m, 1, 6.79m)],
            Lista(corpo, "vendedores"));
        Assert.Equal(
            [new Item("Cliente Y", 225.50m, 2, 60.05m), new Item("Cliente X", 100.00m, 1, 26.63m),
                new Item("Não informado", 50.00m, 1, 13.32m)],
            Lista(corpo, "clientes"));
        Assert.Equal([new Item("Filial", 225.50m, 2, 60.05m), new Item("Matriz", 150.00m, 2, 39.95m)],
            Lista(corpo, "empresas"));
    }

    [Fact]
    public async Task Vendas_sem_vendedor_e_sem_cliente_ficam_em_Nao_informado()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Rankings Sem Nome"));
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 1, VendasSemeadas.Sp(10, 12, 10, 0), 30.00m),
            VendasSemeadas.Venda(c.Filial, 2, VendasSemeadas.Sp(10, 13, 10, 0), 20.00m, "Contingência"));

        var corpo = await RankingsAsync(c);

        Assert.Equal([new Item("Não informado", 50.00m, 2, 100.00m)], Lista(corpo, "vendedores"));
        Assert.Equal([new Item("Não informado", 50.00m, 2, 100.00m)], Lista(corpo, "clientes"));
    }

    [Fact]
    public async Task Empate_de_faturamento_ordena_por_nome_em_ordem_alfabetica()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Rankings Empate"));
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 1, VendasSemeadas.Sp(10, 12, 10, 0), 50.00m, vendedorCodigo: 1, vendedorNome: "Carlos"),
            VendasSemeadas.Venda(c.Matriz, 2, VendasSemeadas.Sp(10, 12, 11, 0), 50.00m, vendedorCodigo: 2, vendedorNome: "Álvaro"),
            VendasSemeadas.Venda(c.Matriz, 3, VendasSemeadas.Sp(10, 12, 12, 0), 50.00m, vendedorCodigo: 3, vendedorNome: "Bruna"),
            VendasSemeadas.Venda(c.Matriz, 4, VendasSemeadas.Sp(10, 12, 13, 0), 70.00m, vendedorCodigo: 4, vendedorNome: "Zélia"));

        var corpo = await RankingsAsync(c);

        Assert.Equal(["Zélia", "Álvaro", "Bruna", "Carlos"], Lista(corpo, "vendedores").Select(v => v.Nome));
    }

    [Fact]
    public async Task Empresa_renomeada_a_mao_aparece_com_o_nome_manual()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Rankings Renomeada"));
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, c.Tenant);
        using var renomear = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, $"/api/empresas/{c.Filial.IdPublico}",
            Acesso.Token(admin, c.Tenant), new { nome = "Loja do Centro" }));
        Assert.Equal(HttpStatusCode.OK, renomear.StatusCode);

        var corpo = await RankingsAsync(c);

        Assert.Equal(["Loja do Centro", "Matriz"], Lista(corpo, "empresas").Select(e => e.Nome));
    }

    [Fact]
    public async Task Soma_das_participacoes_nao_passa_de_100()
    {
        var corpo = await RankingsAsync(await DozeVendedoresAsync());

        var vendedores = Lista(corpo, "vendedores");
        Assert.Equal(780.00m, corpo.GetProperty("totalFaturamento").GetDecimal());
        // Fora do top 10: vendedores 1 e 2 (30 de 780). Cada participação = faturamento / 780 × 100, 2 casas.
        Assert.All(vendedores, v => Assert.Equal(Math.Round(v.Faturamento / 780m * 100, 2, MidpointRounding.AwayFromZero), v.Participacao));
        Assert.True(vendedores.Sum(v => v.Participacao) <= 100m);
    }

    [Fact]
    public async Task Filtro_com_uma_empresa_restringe_os_rankings_as_vendas_dela()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Rankings Filial"));

        var corpo = await RankingsAsync(c, $"inicio=2026-10-11&fim=2026-10-20&empresas={c.Filial.IdPublico}");

        // Filial no período: 5 (Ana, Cliente Y, 200) e 6 (sem vendedor, Cliente Y, 25,50); total 225,50.
        Assert.Equal(225.50m, corpo.GetProperty("totalFaturamento").GetDecimal());
        Assert.Equal([new Item("Ana", 200.00m, 1, 88.69m), new Item("Não informado", 25.50m, 1, 11.31m)],
            Lista(corpo, "vendedores"));
        Assert.Equal([new Item("Cliente Y", 225.50m, 2, 100.00m)], Lista(corpo, "clientes"));
        Assert.Equal([new Item("Filial", 225.50m, 2, 100.00m)], Lista(corpo, "empresas"));
    }
}
