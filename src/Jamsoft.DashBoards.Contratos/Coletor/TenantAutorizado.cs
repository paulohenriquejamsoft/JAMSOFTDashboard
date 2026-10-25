namespace Jamsoft.DashBoards.Contratos.Coletor;

/// <summary>Tenant que a credencial do Coletor pode alimentar.</summary>
public sealed record TenantAutorizado(Guid IdPublico, string Nome);
