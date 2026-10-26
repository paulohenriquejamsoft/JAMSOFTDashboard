using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Painel;

/// <summary><c>GET /api/painel/kpis</c> (DASH-01, DASH-02, DASH-03, DASH-05, DASH-06, DASH-09).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class KpisEndpointTestes(BancoDeTestes banco) : IAsyncLifetime
{
    /// <summary>Hoje = 20/10/2026 em São Paulo.</summary>
    private readonly FakeTimeProvider _tempo = new(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco).WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(_tempo)));
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<(Tenant Tenant, string Token)> TenantComVendasAsync(Perfil perfil = Perfil.Admin)
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente KPIs");
        await VendasSemeadas.SemearAsync(banco, tenant);
        var usuario = await Dados.NovoUsuarioAsync(banco, perfil, tenant);
        return (tenant, Acesso.Token(usuario, tenant));
    }

    private Task<HttpResponseMessage> GetAsync(string query, string token, Guid? xTenant = null) =>
        _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, $"/api/painel/kpis{query}", token, xTenant));

    private static string Periodo(DateOnly inicio, DateOnly fim) => $"?inicio={inicio:yyyy-MM-dd}&fim={fim:yyyy-MM-dd}";

    private static void AssertIndicador(JsonElement kpis, string nome, decimal atual, decimal anterior, decimal? variacao)
    {
        var indicador = kpis.GetProperty(nome);
        Assert.Equal(atual, indicador.GetProperty("atual").GetDecimal());
        Assert.Equal(anterior, indicador.GetProperty("anterior").GetDecimal());
        var v = indicador.GetProperty("variacao");
        if (variacao is null) Assert.Equal(JsonValueKind.Null, v.ValueKind);
        else Assert.Equal(variacao.Value, v.GetDecimal());
    }

    [Fact]
    public async Task Cada_KPI_e_igual_ao_valor_calculado_a_mao_com_periodo_anterior_e_variacao()
    {
        var (_, token) = await TenantComVendasAsync();

        using var resposta = await GetAsync(Periodo(VendasSemeadas.Inicio, VendasSemeadas.Fim), token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("2026-10-11", corpo.GetProperty("periodo").GetProperty("inicio").GetString());
        Assert.Equal("2026-10-20", corpo.GetProperty("periodo").GetProperty("fim").GetString());
        Assert.Equal("2026-10-01", corpo.GetProperty("anterior").GetProperty("inicio").GetString());
        Assert.Equal("2026-10-10", corpo.GetProperty("anterior").GetProperty("fim").GetString());
        Assert.False(corpo.GetProperty("semVendas").GetBoolean());
        var kpis = corpo.GetProperty("kpis");
        // Contingência conta; Inutilizada não; Cancelada só no %.
        AssertIndicador(kpis, "faturamento", VendasSemeadas.Faturamento, VendasSemeadas.FaturamentoAnterior, 275.50m);
        AssertIndicador(kpis, "vendas", VendasSemeadas.Vendas, VendasSemeadas.VendasAnterior, 100.00m);
        // (93,875 − 50) / 50 × 100, sobre o valor sem arredondar.
        AssertIndicador(kpis, "ticketMedio", VendasSemeadas.TicketMedio, VendasSemeadas.TicketMedioAnterior, 87.75m);
        AssertIndicador(kpis, "itensPorVenda", VendasSemeadas.ItensPorVenda, VendasSemeadas.ItensPorVendaAnterior, 33.33m);
        AssertIndicador(kpis, "percentualCanceladas", VendasSemeadas.PercentualCanceladas,
            VendasSemeadas.PercentualCanceladasAnterior, null);
    }

    [Fact]
    public async Task Filtro_com_uma_empresa_restringe_os_KPIs_as_vendas_dela()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente KPIs Filial");
        var c = await VendasSemeadas.SemearAsync(banco, tenant);
        var token = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant), tenant);

        using var resposta = await GetAsync(
            $"{Periodo(VendasSemeadas.Inicio, VendasSemeadas.Fim)}&empresas={c.Filial.IdPublico}", token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var kpis = (await Sessao.JsonAsync(resposta)).GetProperty("kpis");
        // Filial: período 5 (200, 4 itens) e 6 (25,50, 1 item); anterior 9 (60, 1 item). Sem canceladas.
        AssertIndicador(kpis, "faturamento", 225.50m, 60.00m, 275.83m);
        AssertIndicador(kpis, "vendas", 2, 1, 100.00m);
        AssertIndicador(kpis, "ticketMedio", 112.75m, 60.00m, 87.92m);
        AssertIndicador(kpis, "itensPorVenda", 2.50m, 1.00m, 150.00m);
        AssertIndicador(kpis, "percentualCanceladas", 0m, 0m, null);
    }

    [Fact]
    public async Task Sem_datas_usa_o_mes_corrente_ate_hoje()
    {
        var (_, token) = await TenantComVendasAsync();

        using var resposta = await GetAsync("", token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("2026-10-01", corpo.GetProperty("periodo").GetProperty("inicio").GetString());
        Assert.Equal("2026-10-20", corpo.GetProperty("periodo").GetProperty("fim").GetString());
        // Efetivadas de 01/10 a 20/10: 1, 2, 5, 6, 8, 9. Anterior (11/09..30/09): 10.
        AssertIndicador(corpo.GetProperty("kpis"), "faturamento", 475.50m, 500.00m, -4.90m);
        AssertIndicador(corpo.GetProperty("kpis"), "vendas", 6, 1, 500.00m);
    }

    [Fact]
    public async Task Periodo_alterado_recalcula_e_variacao_com_anterior_zero_e_nula()
    {
        var (_, token) = await TenantComVendasAsync();

        // 15/10: venda 5 (Filial, 200, 4 itens) efetivada, 3 cancelada, 4 inutilizada. Anterior (14/10): nada.
        using var resposta = await GetAsync(Periodo(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15)), token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var kpis = (await Sessao.JsonAsync(resposta)).GetProperty("kpis");
        AssertIndicador(kpis, "faturamento", 200.00m, 0m, null);
        AssertIndicador(kpis, "vendas", 1, 0, null);
        AssertIndicador(kpis, "ticketMedio", 200.00m, 0m, null);
        AssertIndicador(kpis, "itensPorVenda", 4.00m, 0m, null);
        AssertIndicador(kpis, "percentualCanceladas", 50.00m, 0m, null);
    }

    [Fact]
    public async Task Periodo_sem_vendas_responde_zeros_e_semVendas_verdadeiro()
    {
        var (_, token) = await TenantComVendasAsync();

        using var resposta = await GetAsync(Periodo(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.True(corpo.GetProperty("semVendas").GetBoolean());
        foreach (var nome in new[] { "faturamento", "vendas", "ticketMedio", "itensPorVenda", "percentualCanceladas" })
            AssertIndicador(corpo.GetProperty("kpis"), nome, 0m, 0m, null);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-09")]
    [InlineData("2024-10-01", "2026-10-01")]
    public async Task Periodo_invalido_responde_400_VALIDACAO_com_erro_em_fim(string inicio, string fim)
    {
        var (_, token) = await TenantComVendasAsync();

        using var resposta = await GetAsync($"?inicio={inicio}&fim={fim}", token);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty("fim", out _));
    }

    [Theory]
    [InlineData(Perfil.Consulta)]
    [InlineData(Perfil.Admin)]
    public async Task Consulta_e_Admin_recebem_200(Perfil perfil)
    {
        var (_, token) = await TenantComVendasAsync(perfil);

        using var resposta = await GetAsync(Periodo(VendasSemeadas.Inicio, VendasSemeadas.Fim), token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        AssertIndicador((await Sessao.JsonAsync(resposta)).GetProperty("kpis"), "faturamento",
            VendasSemeadas.Faturamento, VendasSemeadas.FaturamentoAnterior, 275.50m);
    }

    [Fact]
    public async Task SuperAdmin_sem_tenant_responde_400_TENANT_NAO_SELECIONADO()
    {
        var token = await Acesso.TokenSuperAdminAsync(banco);

        using var resposta = await GetAsync(Periodo(VendasSemeadas.Inicio, VendasSemeadas.Fim), token);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("TENANT_NAO_SELECIONADO", await Sessao.CodigoAsync(resposta));
    }
}
