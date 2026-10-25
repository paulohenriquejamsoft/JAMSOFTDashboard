namespace Jamsoft.DashBoards.Contratos.Coletor;

/// <summary>Client credentials do Coletor para <c>POST /api/coletor/token</c> (COL-03).</summary>
public sealed record TokenColetorRequisicao(Guid ClientId, string ClientSecret);
