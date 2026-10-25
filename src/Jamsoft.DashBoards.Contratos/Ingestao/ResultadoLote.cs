namespace Jamsoft.DashBoards.Contratos.Ingestao;

/// <summary>Resposta 200 da ingestão de um lote (ING-01, ING-06).</summary>
public sealed record ResultadoLote(Guid BatchId, int Inseridos, int Atualizados, IReadOnlyList<VendaRejeitada> Rejeitados);

/// <summary>Venda recusada por violar o contrato, com o motivo (ING-06).</summary>
public sealed record VendaRejeitada(int VendaId, int CodEmpresa, string Motivo);
