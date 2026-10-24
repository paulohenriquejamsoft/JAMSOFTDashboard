using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados;

/// <summary>Tentativa de gravar dado de outro tenant (ISO-02).</summary>
public sealed class AcessoCruzadoException(string entidade, int tenantDaEntidade, int tenantAtual)
    : Exception($"Gravação de {entidade} do tenant {tenantDaEntidade} recusada no contexto do tenant {tenantAtual}.")
{
    public string Entidade { get; } = entidade;
    public int TenantDaEntidade { get; } = tenantDaEntidade;
}

/// <summary>
/// Protege a escrita por tenant (ISO-02): em inserções com <c>TenantId</c> zero preenche o tenant atual;
/// qualquer inserção, alteração ou exclusão de <see cref="IPertenceAoTenant"/> de outro tenant lança
/// <see cref="AcessoCruzadoException"/> antes de chegar ao banco. Só
/// <see cref="PlataformaDbContext.OperacaoGlobal"/> libera, de forma explícita.
/// </summary>
public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly TenantSaveChangesInterceptor Instancia = new();

    private TenantSaveChangesInterceptor() { }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Validar(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Validar(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Validar(DbContext? contexto)
    {
        if (contexto is not PlataformaDbContext banco) return;

        var tenantAtual = banco.TenantAtual;
        foreach (var entrada in banco.ChangeTracker.Entries<IPertenceAoTenant>())
        {
            if (entrada.State == EntityState.Added && entrada.Entity.TenantId == 0 && tenantAtual.Id is { } id)
                entrada.Entity.TenantId = id;

            if (banco.EmOperacaoGlobal) continue;

            var tenantId = entrada.Property(e => e.TenantId);
            var permitido = tenantAtual.IdOuSemAcesso;
            var cruzado = entrada.State switch
            {
                EntityState.Added => tenantId.CurrentValue != permitido,
                EntityState.Modified => tenantId.CurrentValue != permitido || tenantId.OriginalValue != permitido,
                EntityState.Deleted => tenantId.OriginalValue != permitido,
                _ => false,
            };
            if (cruzado)
                throw new AcessoCruzadoException(entrada.Metadata.ClrType.Name, tenantId.CurrentValue, permitido);
        }
    }
}
