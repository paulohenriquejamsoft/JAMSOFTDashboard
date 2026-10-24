using Jamsoft.DashBoards.Api.Seguranca;

namespace Jamsoft.DashBoards.Api.Modulos.Tenants;

/// <summary>Gestão de tenants em <c>/api/admin/tenants</c>: somente SuperAdmin (TEN-08).</summary>
public static class TenantsEndpoints
{
    public static IEndpointRouteBuilder MapTenants(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/admin/tenants").RequireAuthorization(Policies.SuperAdmin);
        grupo.MapPost("/", CriarTenantEndpoint.CriarAsync);
        return app;
    }
}
