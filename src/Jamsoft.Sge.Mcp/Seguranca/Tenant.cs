using Microsoft.AspNetCore.Http;

namespace Jamsoft.Sge.Mcp.Seguranca;

/// <summary>Cliente identificado pela api-key, com a connection string já decifrada.</summary>
public sealed record Tenant(int ChaveId, string Cliente, string ConnectionString);

public static class TenantHttpContextExtensions
{
    private const string ItemKey = "Jamsoft.Sge.Mcp.Tenant";

    public static void DefinirTenant(this HttpContext contexto, Tenant tenant) => contexto.Items[ItemKey] = tenant;

    /// <summary>Devolve o cliente autenticado na requisição atual ou lança se não houver.</summary>
    public static Tenant ObterTenant(this HttpContext? contexto) =>
        contexto?.Items[ItemKey] as Tenant
        ?? throw new InvalidOperationException("Requisição sem cliente autenticado.");
}
