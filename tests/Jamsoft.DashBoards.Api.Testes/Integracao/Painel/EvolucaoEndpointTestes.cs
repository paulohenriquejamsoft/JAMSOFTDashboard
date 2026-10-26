using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Painel;

/// <summary><c>GET /api/painel/evolucao</c> (TRD-01, TRD-02, TRD-03).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class EvolucaoEndpointTestes(BancoDeTestes banco) : IAsyncLifetime
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

    private sealed record Ponto(DateOnly Data, decimal Atual, decimal Anterior);

    private async Task<(string Granularidade, List<Ponto> Pontos)> EvolucaoAsync(ConjuntoVendas c, string inicio, string fim,
        string extra = "")
    {
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, c.Tenant);
        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get,
            $"/api/painel/evolucao?inicio={inicio}&fim={fim}{extra}", Acesso.Token(usuario, c.Tenant)));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        var pontos = corpo.GetProperty("pontos").EnumerateArray().Select(p => new Ponto(
            DateOnly.Parse(p.GetProperty("data").GetString()!), p.GetProperty("atual").GetDecimal(),
            p.GetProperty("anterior").GetDecimal())).ToList();
        return (corpo.GetProperty("granularidade").GetString()!, pontos);
    }

    private static DateOnly D(int ano, int mes, int dia) => new(ano, mes, dia);

    [Fact]
    public async Task Dez_dias_com_vendas_em_3_tem_10_pontos_e_7_zeros()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Evolução"));
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 1, VendasSemeadas.Sp(10, 1, 9, 0), 10.00m),
            VendasSemeadas.Venda(c.Matriz, 2, VendasSemeadas.Sp(10, 1, 18, 0), 5.25m, "Contingência"),
            VendasSemeadas.Venda(c.Filial, 3, VendasSemeadas.Sp(10, 4, 12, 0), 20.00m),
            VendasSemeadas.Venda(c.Matriz, 4, VendasSemeadas.Sp(10, 5, 12, 0), 99.00m, "Cancelada"),
            VendasSemeadas.Venda(c.Matriz, 5, VendasSemeadas.Sp(10, 6, 12, 0), 77.00m, "Inutilizada"),
            VendasSemeadas.Venda(c.Filial, 6, VendasSemeadas.Sp(10, 10, 12, 0), 30.00m));

        var (granularidade, pontos) = await EvolucaoAsync(c, "2026-10-01", "2026-10-10");

        Assert.Equal("dia", granularidade);
        Assert.Equal(Enumerable.Range(1, 10).Select(d => D(2026, 10, d)), pontos.Select(p => p.Data));
        Assert.Equal([15.25m, 0, 0, 20.00m, 0, 0, 0, 0, 0, 30.00m], pontos.Select(p => p.Atual));
        Assert.Equal(7, pontos.Count(p => p.Atual == 0));
    }

    [Fact]
    public async Task Venda_as_23h30_de_Sao_Paulo_cai_no_dia_local_e_nao_no_dia_UTC()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Evolução Fuso"));

        var (_, pontos) = await EvolucaoAsync(c, "2026-10-11", "2026-10-20");

        // Venda 2: 12/10 23:30 SP = 13/10 02:30 UTC. Venda 6: 20/10 23:59 SP = 21/10 02:59 UTC.
        Assert.Equal(50.00m, pontos.Single(p => p.Data == D(2026, 10, 12)).Atual);
        Assert.Equal(0m, pontos.Single(p => p.Data == D(2026, 10, 13)).Atual);
        Assert.Equal(25.50m, pontos.Single(p => p.Data == D(2026, 10, 20)).Atual);
    }

    [Fact]
    public async Task Noventa_dias_agrupa_por_mes_com_meses_vazios_zerados()
    {
        var c = await VendasSemeadas.PrepararAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Evolução Mensal"));
        await VendasSemeadas.InserirAsync(banco, c,
            VendasSemeadas.Venda(c.Matriz, 1, VendasSemeadas.Sp(6, 10, 12, 0), 100.00m),
            VendasSemeadas.Venda(c.Matriz, 2, VendasSemeadas.Sp(6, 20, 12, 0), 0.50m),
            // 31/07 23:30 SP = 01/08 02:30 UTC: conta em julho.
            VendasSemeadas.Venda(c.Filial, 3, VendasSemeadas.Sp(7, 31, 23, 30), 30.00m));

        // 01/06 a 29/08: 90 dias.
        var (granularidade, pontos) = await EvolucaoAsync(c, "2026-06-01", "2026-08-29");

        Assert.Equal("mes", granularidade);
        Assert.Equal([new Ponto(D(2026, 6, 1), 100.50m, 0), new Ponto(D(2026, 7, 1), 30.00m, 0), new Ponto(D(2026, 8, 1), 0, 0)],
            pontos);
    }

    [Fact]
    public async Task Serie_anterior_tem_o_mesmo_numero_de_pontos_alinhados_por_posicao()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Evolução Anterior"));

        var (_, pontos) = await EvolucaoAsync(c, "2026-10-11", "2026-10-20");

        // Anterior 01/10..10/10: venda 9 (05/10, 60) ↔ 15/10; venda 8 (10/10 23:59, 40) ↔ 20/10.
        Assert.Equal(10, pontos.Count);
        Assert.Equal([0, 0, 0, 0, 60.00m, 0, 0, 0, 0, 40.00m], pontos.Select(p => p.Anterior));
        Assert.Equal([100.00m, 50.00m, 0, 0, 200.00m, 0, 0, 0, 0, 25.50m], pontos.Select(p => p.Atual));
    }

    [Fact]
    public async Task Filtro_com_uma_empresa_restringe_as_duas_series_as_vendas_dela()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Evolução Filial"));

        var (_, pontos) = await EvolucaoAsync(c, "2026-10-11", "2026-10-20", $"&empresas={c.Filial.IdPublico}");

        // Filial: 5 (15/10, 200) e 6 (20/10, 25,50); anterior 9 (05/10, 60) ↔ 15/10.
        Assert.Equal([0, 0, 0, 0, 200.00m, 0, 0, 0, 0, 25.50m], pontos.Select(p => p.Atual));
        Assert.Equal([0, 0, 0, 0, 60.00m, 0, 0, 0, 0, 0], pontos.Select(p => p.Anterior));
    }
}
