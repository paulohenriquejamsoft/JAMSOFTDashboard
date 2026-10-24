using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Jamsoft.DashBoards.Api.Seguranca;

/// <summary>
/// CTX-02: SuperAdmin acessando uma rota tenant-scoped com <c>X-Tenant</c> gera o registro ACESSO_CONTEXTO_TENANT
/// (SuperAdmin, tenant, rota). Para não inundar a AUDITORIA, no máximo 1 registro por (SuperAdmin, tenant,
/// template da rota) a cada <see cref="Intervalo"/>, contado pelo <see cref="TimeProvider"/>. Outros perfis não geram
/// registro. Fica nas rotas junto do <see cref="ValidacaoContextoTenantFilter"/>.
/// </summary>
public sealed class AuditoriaContextoSuperAdminFilter : IEndpointFilter
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(10);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate next)
    {
        var http = contexto.HttpContext;
        var servicos = http.RequestServices;
        var tenantAtual = servicos.GetRequiredService<ITenantAtual>();

        if (tenantAtual is { Perfil: Perfil.SuperAdmin, IdPublico: { } tenant, UsuarioIdPublico: { } superAdmin })
        {
            var rota = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value ?? "";
            var cache = servicos.GetRequiredService<IMemoryCache>();
            var agora = servicos.GetRequiredService<TimeProvider>().GetUtcNow();
            var chave = ("acesso-contexto-tenant", superAdmin, tenant, rota);

            if (!cache.TryGetValue(chave, out DateTimeOffset proximo) || agora >= proximo)
            {
                // A validade é conferida pelo TimeProvider (testável); a expiração do IMemoryCache só libera memória.
                cache.Set(chave, agora + Intervalo, Intervalo);
                var nome = await servicos.GetRequiredService<PlataformaDbContext>().Tenants.AsNoTracking()
                    .Where(t => t.IdPublico == tenant).Select(t => t.Nome).SingleOrDefaultAsync(http.RequestAborted);
                await servicos.GetRequiredService<IAuditoria>().RegistrarAgoraAsync(OperacaoAuditada.AcessoContextoTenant,
                    recursoIdPublico: null, tenant, nome, ResultadoAuditoria.Sucesso,
                    new { metodo = http.Request.Method, rota }, http.RequestAborted);
            }
        }
        return await next(contexto);
    }
}

public static class AuditoriaContextoSuperAdminExtensoes
{
    /// <summary>Aplica o <see cref="AuditoriaContextoSuperAdminFilter"/> à rota ou ao grupo tenant-scoped.</summary>
    public static TBuilder AuditarContextoSuperAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new AuditoriaContextoSuperAdminFilter());
}
