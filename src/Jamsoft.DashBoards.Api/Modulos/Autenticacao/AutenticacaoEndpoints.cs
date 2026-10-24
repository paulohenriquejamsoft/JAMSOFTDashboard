using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

/// <summary>Registro do módulo de autenticação (rotas em <c>/api/auth</c>).</summary>
public static class AutenticacaoEndpoints
{
    public static IServiceCollection AddAutenticacao(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
        services.AddScoped<RefreshTokens>();
        return services;
    }

    public static IEndpointRouteBuilder MapAutenticacao(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth");
        grupo.MapPost("/login", LoginEndpoint.EntrarAsync).AllowAnonymous();
        return app;
    }
}
