using Jamsoft.DashBoards.Api.Seguranca;

namespace Jamsoft.DashBoards.Api.Modulos.Exclusao;

/// <summary>Rotas da exclusão de tenant: somente SuperAdmin (DEL-01).</summary>
public static class ExclusoesEndpoints
{
    public static IEndpointRouteBuilder MapExclusao(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/tenants/{idPublico}/exclusao", SolicitarExclusaoEndpoint.SolicitarAsync)
            .RequireAuthorization(Policies.SuperAdmin);
        return app;
    }
}
