using System.Threading.RateLimiting;
using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

/// <summary>Registro do módulo de autenticação (rotas em <c>/api/auth</c>).</summary>
public static class AutenticacaoEndpoints
{
    /// <summary>Policy do rate limiter aplicada ao login: <see cref="AcessoOptions.LimiteLoginPorMinuto"/> por IP.</summary>
    public const string LimiteLogin = "login";

    public static IServiceCollection AddAutenticacao(this IServiceCollection services)
    {
        services.AddOptions<AcessoOptions>()
            .BindConfiguration(AcessoOptions.Secao)
            .Validate(o => o.Valida(), "Acesso: tentativas, minutos de bloqueio e limite de login precisam ser positivos.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
        services.AddScoped<RefreshTokens>();
        services.AddScoped<Bloqueio>();

        services.AddRateLimiter(o =>
        {
            o.AddPolicy(LimiteLogin, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = http.RequestServices.GetRequiredService<IOptions<AcessoOptions>>().Value.LimiteLoginPorMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            o.OnRejected = (contexto, _) => new ValueTask(ProblemasApi.Problema(StatusCodes.Status429TooManyRequests,
                "MUITAS_REQUISICOES", "Aguarde um minuto").ExecuteAsync(contexto.HttpContext));
        });
        return services;
    }

    public static IApplicationBuilder UseAutenticacao(this IApplicationBuilder app) => app.UseRateLimiter();

    public static IEndpointRouteBuilder MapAutenticacao(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth");
        grupo.MapPost("/login", LoginEndpoint.EntrarAsync).AllowAnonymous().RequireRateLimiting(LimiteLogin);
        return app;
    }
}
