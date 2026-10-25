namespace Jamsoft.DashBoards.Api.Modulos.Painel;

public sealed record Periodo(DateOnly Inicio, DateOnly Fim);

/// <summary>Valor no período, no anterior e a variação % (nula quando o anterior é 0) (DASH-09).</summary>
public sealed record Indicador(decimal Atual, decimal Anterior, decimal? Variacao);

public sealed record Kpis(Indicador Faturamento, Indicador Vendas, Indicador TicketMedio, Indicador ItensPorVenda,
    Indicador PercentualCanceladas);

// SPEC_DEVIATION: os KPIs vêm aninhados em "kpis" (tabela de rotas do design), não como propriedades de KpisResposta
// (record de Data Models).
// Reason: as duas seções do design divergem; a tabela de rotas é o contrato que o frontend consome.
public sealed record KpisResposta(Periodo Periodo, Periodo Anterior, bool SemVendas, Kpis Kpis);

/// <summary><c>Data</c>: o dia, ou o dia 1 do mês; <c>Rotulo</c>: dd/MM ou MM/yyyy.</summary>
public sealed record PontoEvolucao(string Rotulo, DateOnly Data, decimal Atual, decimal Anterior);

/// <summary><c>Granularidade</c>: "dia" ou "mes".</summary>
public sealed record EvolucaoResposta(string Granularidade, IReadOnlyList<PontoEvolucao> Pontos);

/// <summary><c>Participacao</c>: % do faturamento total do período (RNK-02).</summary>
public sealed record ItemRanking(string Nome, decimal Faturamento, int Vendas, decimal Participacao);

public sealed record RankingsResposta(decimal TotalFaturamento, IReadOnlyList<ItemRanking> Vendedores,
    IReadOnlyList<ItemRanking> Clientes, IReadOnlyList<ItemRanking> Empresas);
