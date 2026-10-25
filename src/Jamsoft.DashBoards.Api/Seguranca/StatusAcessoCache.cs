using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Jamsoft.DashBoards.Api.Seguranca;

/// <summary>
/// Situação (ativo/inativo) de usuários e tenants, consultada a cada requisição autenticada e guardada
/// por 60 s (AUTH-07, USR-06, TEN-05). Quem inativa chama <see cref="InvalidarUsuario"/> ou
/// <see cref="InvalidarTenant"/> para o efeito ser imediato.
/// </summary>
public sealed class StatusAcessoCache(IMemoryCache cache, IServiceScopeFactory escopos, TimeProvider tempo)
{
    public static readonly TimeSpan Validade = TimeSpan.FromSeconds(60);

    private sealed record Entrada(bool Ativo, DateTimeOffset ExpiraEm);

    /// <summary>Usuário existe e está ativo e, quando informado, o tenant dele também.</summary>
    public async Task<bool> AtivoAsync(Guid usuarioIdPublico, Guid? tenantIdPublico, CancellationToken ct = default)
    {
        var usuarioAtivo = await ObterAsync(ChaveUsuario(usuarioIdPublico), banco => banco.Usuarios
            .Where(u => u.IdPublico == usuarioIdPublico).Select(u => u.Ativo).SingleOrDefaultAsync(ct));
        if (!usuarioAtivo || tenantIdPublico is not { } tenant) return usuarioAtivo;

        return await ObterAsync(ChaveTenant(tenant), banco => banco.Tenants
            .Where(t => t.IdPublico == tenant).Select(t => t.Ativo).SingleOrDefaultAsync(ct));
    }

    /// <summary>Tenant existe e está ativo (ingestão do Coletor, ING-03).</summary>
    public Task<bool> TenantAtivoAsync(Guid tenantIdPublico, CancellationToken ct = default) =>
        ObterAsync(ChaveTenant(tenantIdPublico), banco => banco.Tenants
            .Where(t => t.IdPublico == tenantIdPublico).Select(t => t.Ativo).SingleOrDefaultAsync(ct));

    public void InvalidarUsuario(Guid usuarioIdPublico) => cache.Remove(ChaveUsuario(usuarioIdPublico));

    public void InvalidarTenant(Guid tenantIdPublico) => cache.Remove(ChaveTenant(tenantIdPublico));

    private static (string, Guid) ChaveUsuario(Guid id) => ("status-usuario", id);
    private static (string, Guid) ChaveTenant(Guid id) => ("status-tenant", id);

    private async Task<bool> ObterAsync((string, Guid) chave, Func<PlataformaDbContext, Task<bool>> consulta)
    {
        // A validade é conferida pelo TimeProvider (testável); a expiração do IMemoryCache só libera memória.
        var agora = tempo.GetUtcNow();
        if (cache.TryGetValue(chave, out Entrada? entrada) && entrada!.ExpiraEm > agora) return entrada.Ativo;

        await using var escopo = escopos.CreateAsyncScope();
        var ativo = await consulta(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>());
        cache.Set(chave, new Entrada(ativo, agora + Validade), Validade);
        return ativo;
    }
}
