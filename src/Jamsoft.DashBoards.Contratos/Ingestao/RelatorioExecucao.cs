namespace Jamsoft.DashBoards.Contratos.Ingestao;

/// <summary>Fim de uma execução do Coletor, enviado para <c>POST /api/coletor/execucoes</c> (ING-09).</summary>
public sealed record RelatorioExecucao(Guid Tenant, Guid ConfiguracaoId, string Rotina, string? BancoOrigem,
    DateTimeOffset Inicio, DateTimeOffset Fim, string Situacao, int RegistrosEnviados, string? Mensagem);
