using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Painel;

/// <summary>
/// Painel de vendas em <c>/api/painel</c>, um endpoint por bloco, todos com o mesmo filtro
/// (<c>inicio</c>, <c>fim</c>, <c>empresas</c>) e restritos ao tenant efetivo (DASH-07).
/// Vendas efetivadas = Finalizada + Contingência; Inutilizada fica fora de todos os indicadores.
/// </summary>
public static class PainelEndpoints
{
    public const string Finalizada = "Finalizada";
    public const string Contingencia = "Contingência";
    public const string Cancelada = "Cancelada";

    public static IEndpointRouteBuilder MapPainel(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/painel")
            .RequireAuthorization(Policies.LeituraTenant)
            .ValidarContextoTenant()
            .AuditarContextoSuperAdmin();
        grupo.MapGet("/kpis", KpisAsync);
        return app;
    }

    /// <summary>Filtro validado e IDs internos das empresas; <c>Erro</c> preenchido com 400 VALIDACAO (DASH-05).</summary>
    private sealed record Preparado(FiltroPainel Filtro, int[]? EmpresaIds, IResult? Erro);

    /// <summary>Empresa fora do tenant lança <see cref="ProblemaApiException"/> 404 (DASH-08).</summary>
    private static async Task<Preparado> PrepararAsync(DateOnly? inicio, DateOnly? fim, Guid[]? empresas,
        ConsultaVendas consulta, TimeProvider tempo, CancellationToken ct)
    {
        if (!FiltroPainel.TentarCriar(inicio, fim, empresas, FiltroPainel.HojeEmSaoPaulo(tempo), out var filtro, out var erros))
            return new Preparado(filtro, null, ProblemasApi.Validacao(erros));
        return new Preparado(filtro, await consulta.ResolverEmpresasAsync(filtro.Empresas, ct), null);
    }

    public static async Task<IResult> KpisAsync(DateOnly? inicio, DateOnly? fim, Guid[]? empresas,
        ConsultaVendas consulta, TimeProvider tempo, CancellationToken ct)
    {
        var (filtro, empresaIds, erro) = await PrepararAsync(inicio, fim, empresas, consulta, tempo, ct);
        if (erro is not null) return erro;

        var atual = await TotaisAsync(consulta, filtro, empresaIds, ct);
        var anterior = await TotaisAsync(consulta, filtro.Anterior, empresaIds, ct);

        var kpis = new Kpis(
            Indicador(atual.Faturamento, anterior.Faturamento),
            Indicador(atual.Vendas, anterior.Vendas),
            Indicador(atual.TicketMedio, anterior.TicketMedio),
            Indicador(atual.ItensPorVenda, anterior.ItensPorVenda),
            Indicador(atual.PercentualCanceladas, anterior.PercentualCanceladas));
        return Results.Ok(new KpisResposta(
            new Periodo(filtro.Inicio, filtro.Fim),
            new Periodo(filtro.Anterior.Inicio, filtro.Anterior.Fim),
            SemVendas: atual.Vendas == 0 && atual.Canceladas == 0,
            kpis));
    }

    private sealed record Totais(decimal Faturamento, int Vendas, int Itens, int Canceladas)
    {
        public decimal TicketMedio => Vendas == 0 ? 0 : Faturamento / Vendas;
        public decimal ItensPorVenda => Vendas == 0 ? 0 : (decimal)Itens / Vendas;
        public decimal PercentualCanceladas => Vendas + Canceladas == 0 ? 0 : Canceladas * 100m / (Vendas + Canceladas);
    }

    /// <summary>Uma consulta agregada por período, com somas e contagens condicionais (DASH-02).</summary>
    private static async Task<Totais> TotaisAsync(ConsultaVendas consulta, FiltroPainel filtro, int[]? empresaIds,
        CancellationToken ct)
    {
        var totais = await consulta.Do(filtro, empresaIds)
            .GroupBy(_ => 1)
            .Select(g => new Totais(
                g.Sum(v => v.Situacao == Finalizada || v.Situacao == Contingencia ? v.Valor : 0m),
                g.Count(v => v.Situacao == Finalizada || v.Situacao == Contingencia),
                g.Sum(v => v.Situacao == Finalizada || v.Situacao == Contingencia ? v.QtdItens : 0),
                g.Count(v => v.Situacao == Cancelada)))
            .SingleOrDefaultAsync(ct);
        return totais ?? new Totais(0, 0, 0, 0);
    }

    private static Indicador Indicador(decimal atual, decimal anterior) =>
        new(Arredondar(atual), Arredondar(anterior), anterior == 0 ? null : Arredondar((atual - anterior) / anterior * 100));

    /// <summary>2 casas, meio para longe do zero (93,875 → 93,88).</summary>
    public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
