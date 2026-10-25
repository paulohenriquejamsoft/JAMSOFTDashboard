using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Jamsoft.DashBoards.Api.Modulos.Coletores;

/// <summary>
/// Situação da credencial do Coletor (ativa e <c>VERSAO_CREDENCIAL</c>), consultada a cada requisição com token
/// de Coletor e guardada por 60 s (AD-015, COL-05). Token revogado ou com <c>cred_ver</c> antiga deixa de valer
/// nesse prazo; revogação, rotação e troca de tenants chamam <see cref="Invalidar"/> para o efeito ser imediato.
/// </summary>
public sealed class StatusColetorCache(IMemoryCache cache, IServiceScopeFactory escopos, TimeProvider tempo)
{
    public static readonly TimeSpan Validade = TimeSpan.FromSeconds(60);

    private sealed record Entrada(bool Ativo, int VersaoCredencial, DateTimeOffset ExpiraEm);

    /// <summary>A credencial existe, está ativa e a versão é a do token.</summary>
    public async Task<bool> ValidoAsync(Guid coletorIdPublico, int versaoCredencial, CancellationToken ct = default)
    {
        var agora = tempo.GetUtcNow();
        if (!cache.TryGetValue(Chave(coletorIdPublico), out Entrada? entrada) || entrada!.ExpiraEm <= agora)
        {
            await using var escopo = escopos.CreateAsyncScope();
            var banco = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var atual = await banco.Coletores.AsNoTracking().Where(c => c.IdPublico == coletorIdPublico)
                .Select(c => new { c.Ativo, c.VersaoCredencial }).SingleOrDefaultAsync(ct);
            entrada = new Entrada(atual?.Ativo ?? false, atual?.VersaoCredencial ?? 0, agora + Validade);
            cache.Set(Chave(coletorIdPublico), entrada, Validade);
        }
        return entrada.Ativo && entrada.VersaoCredencial == versaoCredencial;
    }

    public void Invalidar(Guid coletorIdPublico) => cache.Remove(Chave(coletorIdPublico));

    private static (string, Guid) Chave(Guid id) => ("status-coletor", id);
}
