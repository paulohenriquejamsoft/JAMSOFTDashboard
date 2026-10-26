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
    public async Task Tres_NFe_cinco_NFCe_e_dois_Pedidos_dividem_faturamento_vendas_e_participacao()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e"));
        var vendas = Enumerable.Range(1, 10)
            .Select(i => VendasSemeadas.Venda(i % 2 == 0 ? c.Matriz : c.Filial, i, VendasSemeadas.Sp(10, 12, 10, i), 100.00m,
                i == 10 ? "Contingência" : "Finalizada", i <= 3 ? "NF-e" : i <= 8 ? "NFC-e" : "Pedido"))
            // Cancelada e Inutilizada não entram.
            .Append(VendasSemeadas.Venda(c.Matriz, 11, VendasSemeadas.Sp(10, 13, 10, 0), 500.00m, "Cancelada", "NF-e"))
            .Append(VendasSemeadas.Venda(c.Matriz, 12, VendasSemeadas.Sp(10, 13, 11, 0), 500.00m, "Inutilizada", "NFC-e"))
            .ToArray();
        await VendasSemeadas.InserirAsync(banco, c, vendas);

        var tipos = await DfeAsync(c);

        Assert.Equal([new Tipo("NF-e", 300.00m, 3, 30.00m), new Tipo("NFC-e", 500.00m, 5, 50.00m), new Tipo("Pedido", 200.00m, 2, 20.00m)],
            tipos);
    }

    [Fact]
    public async Task Participacao_e_por_faturamento_e_nao_por_quantidade_de_vendas()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e Base"));
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 1, VendasSemeadas.Sp(10, 12, 10, 0), 300.00m, tipoDfe: "NF-e"),
            VendasSemeadas.Venda(c.Matriz, 2, VendasSemeadas.Sp(10, 12, 11, 0), 100.00m, tipoDfe: "NFC-e"));

        var tipos = await DfeAsync(c);

        // Uma venda de cada tipo: por quantidade seria 50/50; por faturamento, 300/400 e 100/400.
        Assert.Equal([new Tipo("NF-e", 300.00m, 1, 75.00m), new Tipo("NFC-e", 100.00m, 1, 25.00m), new Tipo("Pedido", 0m, 0, 0m)], tipos);
    }

    [Fact]
    public async Task Periodo_so_com_NFCe_traz_NFe_e_Pedido_com_zeros()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e só NFC-e"));
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 1, VendasSemeadas.Sp(10, 12, 10, 0), 42.10m, tipoDfe: "NFC-e"),
            VendasSemeadas.Venda(c.Filial, 2, VendasSemeadas.Sp(10, 14, 10, 0), 7.90m, tipoDfe: "NFC-e"));

        var tipos = await DfeAsync(c);

        Assert.Equal([new Tipo("NF-e", 0m, 0, 0m), new Tipo("NFC-e", 50.00m, 2, 100.00m), new Tipo("Pedido", 0m, 0, 0m)], tipos);
    }

    [Fact]
    public async Task Filtro_com_uma_empresa_restringe_o_DFe_as_vendas_dela()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e Filial"));

        var tipos = await DfeAsync(c, $"&empresas={c.Filial.IdPublico}");

        // Filial no período: 5 (NF-e, 200) e 6 (NFC-e, 25,50); total 225,50.
        Assert.Equal([new Tipo("NF-e", 200.00m, 1, 88.69m), new Tipo("NFC-e", 25.50m, 1, 11.31m), new Tipo("Pedido", 0m, 0, 0m)],
            tipos);
    }

    [Fact]
    public async Task Filtro_por_vendedor_restringe_o_DFe_as_vendas_dele()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e Vendedor"));

        var tipos = await DfeAsync(c, "&vendedores=2");

        // Bruno (2) no período: só a 2 (NFC-e, 50, Contingência).
        Assert.Equal([new Tipo("NF-e", 0m, 0, 0m), new Tipo("NFC-e", 50.00m, 1, 100.00m), new Tipo("Pedido", 0m, 0, 0m)], tipos);
    }

    [Fact]
    public async Task Tipo_de_documento_invalido_responde_400()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente DF-e Tipo Inválido"));
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, c.Tenant);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get,
            "/api/painel/dfe?inicio=2026-10-11&fim=2026-10-20&tipos=CT-e", Acesso.Token(usuario, c.Tenant)));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Vendedores_lista_codigo_e_ultimo_nome_em_ordem_alfabetica_sem_os_de_outro_tenant()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Vendedores"));
        var outro = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Vendedores Outro"));
        await VendasSemeadas.InserirAsync(banco, outro,
            VendasSemeadas.Venda(outro.Matriz, 1, VendasSemeadas.Sp(10, 12, 10, 0), 10m, vendedorCodigo: 9, vendedorNome: "Zeca"));
        // Ana mudou de nome depois: vale o último recebido.
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 50, VendasSemeadas.Sp(10, 25, 10, 0), 10m, vendedorCodigo: 1, vendedorNome: "Ana Paula"));
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, c.Tenant);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/painel/vendedores",
            Acesso.Token(usuario, c.Tenant)));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var vendedores = (await Sessao.JsonAsync(resposta)).EnumerateArray()
            .Select(v => (v.GetProperty("codigo").GetInt32(), v.GetProperty("nome").GetString())).ToList();
        Assert.Equal([(1, "Ana Paula"), (2, "Bruno")], vendedores);
    }
}
