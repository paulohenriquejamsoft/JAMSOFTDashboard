namespace Jamsoft.Sge.Mcp.Seguranca;

/// <summary>
/// Tenant da plataforma identificado pela api-key. <see cref="Id"/> é o TENANT_ID interno, usado só nas consultas;
/// para fora (respostas e logs) vai o <see cref="IdPublico"/>.
/// </summary>
public sealed record Tenant(int Id, Guid IdPublico, string Nome, string PrefixoChave);

public static class TenantHttpContextExtensions
{
    private const string ItemKey = "Jamsoft.Sge.Mcp.Tenant";

    public static void DefinirTenant(this HttpContext contexto, Tenant tenant) => contexto.Items[ItemKey] = tenant;

    /// <summary>Devolve o tenant autenticado na requisição atual ou lança se não houver.</summary>
    public static Tenant ObterTenant(this HttpContext? contexto) =>
        contexto?.Items[ItemKey] as Tenant
        ?? throw new InvalidOperationException("Requisição sem tenant autenticado.");
}
