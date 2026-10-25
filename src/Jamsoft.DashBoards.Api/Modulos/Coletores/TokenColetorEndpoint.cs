using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Contratos.Coletor;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Coletores;

/// <summary>
/// Rotas do Coletor em <c>/api/coletor</c>. <c>POST /token</c> (COL-03, COL-04, AD-015) troca client credentials
/// por um JWT de 15 minutos com os tenants autorizados e ativos. Credencial inexistente, secret errado e
/// credencial revogada têm a mesma resposta. As demais rotas exigem a policy <see cref="Policies.Coletor"/>.
/// </summary>
public static class TokenColetorEndpoint
{
    public const string CodigoCredencialInvalida = "CREDENCIAL_INVALIDA";

    /// <summary>Policy do rate limiter do token: <see cref="LimitePorMinuto"/> por IP.</summary>
    public const string LimiteToken = "coletor-token";
    public const int LimitePorMinuto = 20;

    public static IServiceCollection AddColetores(this IServiceCollection services)
    {
        services.AddRateLimiter(o => o.AddPolicy(LimiteToken, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = LimitePorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })));
        return services;
    }

    public static IEndpointRouteBuilder MapColetor(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/coletor");
        grupo.MapPost("/token", EmitirAsync).AllowAnonymous().RequireRateLimiting(LimiteToken);
        grupo.MapGet("/tenants", ListarTenantsAsync).RequireAuthorization(Policies.Coletor);
        return app;
    }

    /// <summary>
    /// Tenants autorizados ao token (claim <c>tenants</c>) e ainda ativos, por nome, para a tela de configuração
    /// do Coletor (CFG-02, CFG-05).
    /// </summary>
    public static async Task<IResult> ListarTenantsAsync(ITenantAtual atual, PlataformaDbContext banco, CancellationToken ct)
    {
        var autorizados = atual.TenantsAutorizadosColetor.ToList();
        var tenants = await banco.Tenants.AsNoTracking()
            .Where(t => autorizados.Contains(t.IdPublico) && t.Ativo)
            .OrderBy(t => t.Nome).ThenBy(t => t.Id)
            .Select(t => new TenantAutorizado(t.IdPublico, t.Nome))
            .ToListAsync(ct);
        return Results.Ok(tenants);
    }

    public static IResult CredencialInvalida() =>
        ProblemasApi.Problema(StatusCodes.Status401Unauthorized, CodigoCredencialInvalida, "Credencial inválida ou revogada");

    public static async Task<IResult> EmitirAsync(TokenColetorRequisicao corpo, PlataformaDbContext banco,
        TokenService tokens, TimeProvider tempo, CancellationToken ct)
    {
        var coletor = await banco.Coletores.AsNoTracking().SingleOrDefaultAsync(c => c.ClientId == corpo.ClientId, ct);
        var hash = SegredoColetor.Hash(corpo.ClientSecret ?? "");
        if (coletor is null || !CryptographicOperations.FixedTimeEquals(coletor.SecretHash, hash) || !coletor.Ativo)
            return CredencialInvalida();

        var tenants = await TenantsAtivosAsync(banco, coletor.Id, ct);
        var emitido = tokens.EmitirColetor(coletor, tenants.Select(t => t.IdPublico));

        var agora = tempo.GetUtcNow().UtcDateTime;
        await banco.Coletores.Where(c => c.Id == coletor.Id)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.UltimoTokenEm, agora), ct);

        return Results.Ok(new TokenColetorResposta(emitido.Token, emitido.ExpiraEm, tenants));
    }

    /// <summary>Tenants autorizados ao coletor e ativos, por nome.</summary>
    internal static Task<List<TenantAutorizado>> TenantsAtivosAsync(PlataformaDbContext banco, int coletorId,
        CancellationToken ct) =>
        (from autorizado in banco.ColetoresTenant.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
         join tenant in banco.Tenants on autorizado.TenantId equals tenant.Id
         where autorizado.ColetorId == coletorId && tenant.Ativo
         orderby tenant.Nome, tenant.Id
         select new TenantAutorizado(tenant.IdPublico, tenant.Nome))
        .ToListAsync(ct);
}
