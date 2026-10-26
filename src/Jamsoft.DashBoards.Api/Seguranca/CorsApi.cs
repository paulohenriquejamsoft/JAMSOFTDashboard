using Jamsoft.DashBoards.Api.Configuracao;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Seguranca;

/// <summary>
/// Policy padrão de CORS a partir de <see cref="CorsApiOptions"/>. Libera credenciais porque login, refresh e
/// logout usam o cookie do refresh token (<c>withCredentials</c> no frontend).
/// </summary>
public static class CorsApi
{
    public static IServiceCollection AddCorsApi(this IServiceCollection services)
    {
        services.AddOptions<CorsApiOptions>()
            .BindConfiguration(CorsApiOptions.Secao)
            .Validate(o => o.Valida(), "Cors:OrigensPermitidas aceita só origens http(s) completas, sem caminho nem barra final.")
            .ValidateOnStart();
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<CorsApiOptions>>((cors, api) =>
            {
                if (api.Value.OrigensPermitidas.Length == 0) return;
                cors.AddDefaultPolicy(p => p
                    .WithOrigins(api.Value.OrigensPermitidas)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });
        return services;
    }
}
