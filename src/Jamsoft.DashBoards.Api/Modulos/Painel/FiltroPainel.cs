namespace Jamsoft.DashBoards.Api.Modulos.Painel;

/// <summary>Agrupamento da série de evolução (TRD-01, TRD-02).</summary>
public enum Granularidade
{
    Dia,
    Mes,
}

/// <summary>
/// Período e empresas validados de uma consulta do painel. As datas são inclusivas no fuso de São Paulo e viram
/// a faixa <c>[inicio 00:00, fim+1 00:00)</c> em UTC (AD-018).
/// </summary>
public sealed class FiltroPainel
{
    public const int MesesMaximos = 24;
    public const int DiasMaximosGranularidadeDiaria = 62;

    /// <summary>America/Sao_Paulo ("E. South America Standard Time" no Windows).</summary>
    public static readonly TimeZoneInfo FusoSaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private FiltroPainel(DateOnly inicio, DateOnly fim, Guid[]? empresas)
    {
        Inicio = inicio;
        Fim = fim;
        Empresas = empresas;
    }

    public DateOnly Inicio { get; }
    public DateOnly Fim { get; }
    /// <summary>ID_PUBLICO das empresas selecionadas; nulo ou vazio = todas.</summary>
    public Guid[]? Empresas { get; }

    public DateTimeOffset DesdeUtc => MeiaNoiteUtc(Inicio);
    public DateTimeOffset AteUtcExclusivo => MeiaNoiteUtc(Fim.AddDays(1));
    public int Dias => Fim.DayNumber - Inicio.DayNumber + 1;

    /// <summary>Mesmo número de dias, terminando na véspera de <see cref="Inicio"/> (DASH-09).</summary>
    public FiltroPainel Anterior => new(Inicio.AddDays(-Dias), Inicio.AddDays(-1), Empresas);

    public Granularidade Granularidade => Dias <= DiasMaximosGranularidadeDiaria ? Granularidade.Dia : Granularidade.Mes;

    /// <summary>Todos os dias do período, ou o dia 1 de cada mês tocado por ele, para preencher zeros.</summary>
    public IReadOnlyList<DateOnly> Buckets()
    {
        var buckets = new List<DateOnly>();
        if (Granularidade == Granularidade.Dia)
        {
            for (var dia = Inicio; dia <= Fim; dia = dia.AddDays(1)) buckets.Add(dia);
            return buckets;
        }
        for (var mes = new DateOnly(Inicio.Year, Inicio.Month, 1); mes <= Fim; mes = mes.AddMonths(1)) buckets.Add(mes);
        return buckets;
    }

    /// <summary>Data de hoje em São Paulo.</summary>
    public static DateOnly HojeEmSaoPaulo(TimeProvider tempo) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(tempo.GetUtcNow(), FusoSaoPaulo).DateTime);

    /// <summary>
    /// Sem datas, usa o mês corrente (dia 1 até <paramref name="hoje"/>) (DASH-01). Fim antes do início ou período
    /// acima de 24 meses → erro em <c>fim</c> (DASH-05).
    /// </summary>
    public static bool TentarCriar(DateOnly? inicio, DateOnly? fim, Guid[]? empresas, DateOnly hoje,
        out FiltroPainel filtro, out Dictionary<string, string[]> erros)
    {
        var i = inicio ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var f = fim ?? hoje;
        erros = [];
        if (f < i)
            erros["fim"] = ["A data final não pode ser anterior à inicial."];
        else if (f > i.AddMonths(MesesMaximos).AddDays(-1))
            erros["fim"] = [$"O período não pode passar de {MesesMaximos} meses."];

        filtro = new FiltroPainel(i, f, empresas);
        return erros.Count == 0;
    }

    private static DateTimeOffset MeiaNoiteUtc(DateOnly data)
    {
        var local = data.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, FusoSaoPaulo.GetUtcOffset(local)).ToUniversalTime();
    }
}
