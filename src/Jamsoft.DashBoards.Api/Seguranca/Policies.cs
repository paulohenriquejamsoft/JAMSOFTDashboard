using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Seguranca;

/// <summary>
/// Policies por perfil (§7, AUTH-08). A policy padrão e a de fallback (rotas sem policy explícita)
/// exigem usuário do portal autenticado e sem troca de senha pendente (USR-08). Token de Coletor
/// só atende à policy <see cref="Coletor"/>.
/// </summary>
public static class Policies
{
    public const string SuperAdmin = "SuperAdmin";
    /// <summary>Admin do tenant ou SuperAdmin com tenant selecionado.</summary>
    public const string AdminTenant = "AdminTenant";
    /// <summary>Consulta ou Admin do tenant, ou SuperAdmin com tenant selecionado.</summary>
    public const string LeituraTenant = "LeituraTenant";
    public const string Coletor = "Coletor";

    public const string CodigoTrocaSenhaPendente = "TROCA_SENHA_PENDENTE";
    public const string CodigoTenantNaoSelecionado = "TENANT_NAO_SELECIONADO";

    /// <summary>Registra JwtBearer, emissão de tokens, policies e as respostas 400/403 específicas.</summary>
    public static IServiceCollection AddSegurancaApi(this IServiceCollection services)
    {
        services.AddTokenService();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                // Mantém os nomes das claims do token (sub, perfil, tenant, trocar_senha).
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = TokenService.ParametrosValidacao(jwt.Value);
            });

        services.AddScoped<IAuthorizationHandler, PerfilHandler>();
        services.AddScoped<IAuthorizationHandler, TrocaSenhaConcluidaHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, RespostaAutorizacao>();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(PolicyDeUsuario(new PerfilRequirement([Perfil.SuperAdmin, Perfil.Admin, Perfil.Consulta])))
            .SetFallbackPolicy(PolicyDeUsuario(new PerfilRequirement([Perfil.SuperAdmin, Perfil.Admin, Perfil.Consulta])))
            .AddPolicy(SuperAdmin, PolicyDeUsuario(new PerfilRequirement([Perfil.SuperAdmin])))
            .AddPolicy(AdminTenant, PolicyDeUsuario(new PerfilRequirement([Perfil.Admin], SuperAdminComTenant: true)))
            .AddPolicy(LeituraTenant,
                PolicyDeUsuario(new PerfilRequirement([Perfil.Admin, Perfil.Consulta], SuperAdminComTenant: true)))
            .AddPolicy(Coletor, new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PerfilRequirement([Perfil.Coletor]))
                .Build());
        return services;
    }

    public static IApplicationBuilder UseSegurancaApi(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Libera a rota para quem ainda precisa trocar a senha (troca de senha, logout).</summary>
    public static TBuilder AllowTrocaSenhaPendente<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new PermiteTrocaSenhaPendente());

    private static AuthorizationPolicy PolicyDeUsuario(PerfilRequirement perfil) =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(perfil, new TrocaSenhaConcluidaRequirement())
            .Build();
}

/// <summary>Metadado de rota liberada com troca de senha pendente.</summary>
public sealed class PermiteTrocaSenhaPendente;

/// <summary>Perfil aceito; com <paramref name="SuperAdminComTenant"/>, SuperAdmin passa se tiver tenant selecionado.</summary>
public sealed record PerfilRequirement(IReadOnlyCollection<Perfil> Perfis, bool SuperAdminComTenant = false)
    : IAuthorizationRequirement;

public sealed class TrocaSenhaConcluidaRequirement : IAuthorizationRequirement;

public sealed class PerfilHandler(ITenantAtual tenantAtual) : AuthorizationHandler<PerfilRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PerfilRequirement requisito)
    {
        if (tenantAtual.Perfil is not { } perfil) return Task.CompletedTask;

        if (requisito.Perfis.Contains(perfil))
            context.Succeed(requisito);
        else if (requisito.SuperAdminComTenant && perfil == Perfil.SuperAdmin)
        {
            if (tenantAtual.IdPublico is not null) context.Succeed(requisito);
            else context.Fail(new AuthorizationFailureReason(this, Policies.CodigoTenantNaoSelecionado));
        }
        return Task.CompletedTask;
    }
}

public sealed class TrocaSenhaConcluidaHandler(ITenantAtual tenantAtual) : AuthorizationHandler<TrocaSenhaConcluidaRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TrocaSenhaConcluidaRequirement requisito)
    {
        var liberada = context.Resource is HttpContext http
            && http.GetEndpoint()?.Metadata.GetMetadata<PermiteTrocaSenhaPendente>() is not null;

        if (!tenantAtual.TrocaSenhaPendente || liberada) context.Succeed(requisito);
        else context.Fail(new AuthorizationFailureReason(this, Policies.CodigoTrocaSenhaPendente));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Traduz as recusas com motivo conhecido: troca de senha pendente → 403 TROCA_SENHA_PENDENTE;
/// SuperAdmin sem tenant em rota do tenant → 400 TENANT_NAO_SELECIONADO. As demais seguem o padrão
/// (401 NAO_AUTENTICADO / 403 ACESSO_NEGADO via ProblemDetails do status).
/// </summary>
public sealed class RespostaAutorizacao : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _padrao = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult resultado)
    {
        if (resultado.Forbidden)
        {
            var motivos = resultado.AuthorizationFailure?.FailureReasons.Select(r => r.Message).ToList() ?? [];
            if (motivos.Contains(Policies.CodigoTrocaSenhaPendente))
            {
                await ProblemasApi.Problema(StatusCodes.Status403Forbidden, Policies.CodigoTrocaSenhaPendente,
                    "Troca de senha pendente").ExecuteAsync(context);
                return;
            }
            if (motivos.Contains(Policies.CodigoTenantNaoSelecionado))
            {
                await ProblemasApi.Problema(StatusCodes.Status400BadRequest, Policies.CodigoTenantNaoSelecionado,
                    "Selecione um tenant").ExecuteAsync(context);
                return;
            }
        }
        await _padrao.HandleAsync(next, context, policy, resultado);
    }
}
