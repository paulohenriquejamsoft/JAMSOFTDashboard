namespace Jamsoft.DashBoards.Contratos.Coletor;

/// <summary>Access token do Coletor e os tenants autorizados e ativos (COL-03).</summary>
public sealed record TokenColetorResposta(string AccessToken, DateTimeOffset ExpiraEm, IReadOnlyList<TenantAutorizado> Tenants);
