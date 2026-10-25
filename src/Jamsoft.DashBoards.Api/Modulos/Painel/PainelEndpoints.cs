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

    /// <summary>America/Sao_Paulo no SQL Server (AD-018).</summary>
    public const string FusoSqlServer = "E. South America Standard Time";

    public static IEndpointRouteBuilder MapPainel(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/painel")
            .RequireAuthorization(Policies.LeituraTenant)
            .ValidarContextoTenant()
            .AuditarContextoSuperAdmin();
        grupo.MapGet("/kpis", KpisAsync);
        grupo.MapGet("/evolucao", EvolucaoAsync);
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

    /// <summary>
    /// Faturamento por dia ou mês no fuso de São Paulo, com zeros preenchidos, e a série do período anterior alinhada
    /// por posição (1º bucket do período ↔ 1º bucket do anterior) (TRD-01..03).
    /// </summary>
    public static async Task<IResult> EvolucaoAsync(DateOnly? inicio, DateOnly? fim, Guid[]? empresas,
        ConsultaVendas consulta, TimeProvider tempo, CancellationToken ct)
    {
        var (filtro, empresaIds, erro) = await PrepararAsync(inicio, fim, empresas, consulta, tempo, ct);
        if (erro is not null) return erro;

        var atual = await SerieAsync(consulta, filtro, empresaIds, ct);
        var anterior = await SerieAsync(consulta, filtro.Anterior, empresaIds, ct);
        var buckets = filtro.Buckets();
        // Um período mensal e o anterior podem tocar números diferentes de meses: sobra vira zero, excesso fica fora.
        var bucketsAnteriores = filtro.Anterior.Buckets();
        var diario = filtro.Granularidade == Granularidade.Dia;

        var pontos = buckets.Select((bucket, i) => new PontoEvolucao(
            bucket.ToString(diario ? "dd/MM" : "MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
            bucket,
            Arredondar(atual.GetValueOrDefault(bucket)),
            Arredondar(i < bucketsAnteriores.Count ? anterior.GetValueOrDefault(bucketsAnteriores[i]) : 0))).ToList();
        return Results.Ok(new EvolucaoResposta(diario ? "dia" : "mes", pontos));
    }

    /// <summary>Faturamento das efetivadas por bucket (dia, ou dia 1 do mês) no fuso de São Paulo.</summary>
    private static async Task<Dictionary<DateOnly, decimal>> SerieAsync(ConsultaVendas consulta, FiltroPainel filtro,
        int[]? empresaIds, CancellationToken ct)
    {
        var locais = consulta.Do(filtro, empresaIds)
            .Where(v => v.Situacao == Finalizada || v.Situacao == Contingencia)
            .Select(v => new { Local = EF.Functions.AtTimeZone(v.DataHora, FusoSqlServer), v.Valor });

        if (filtro.Granularidade == Granularidade.Dia)
        {
            var dias = await locais.GroupBy(x => x.Local.Date)
                .Select(g => new { Dia = g.Key, Total = g.Sum(x => x.Valor) })
                .ToListAsync(ct);
            return dias.ToDictionary(d => DateOnly.FromDateTime(d.Dia), d => d.Total);
        }

        var meses = await locais.GroupBy(x => new { x.Local.Year, x.Local.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.Valor) })
            .ToListAsync(ct);
        return meses.ToDictionary(m => new DateOnly(m.Year, m.Month, 1), m => m.Total);
    }

    private static Indicador Indicador(decimal atual, decimal anterior) =>
        new(Arredondar(atual), Arredondar(anterior), anterior == 0 ? null : Arredondar((atual - anterior) / anterior * 100));

    /// <summary>2 casas, meio para longe do zero (93,875 → 93,88).</summary>
    public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
