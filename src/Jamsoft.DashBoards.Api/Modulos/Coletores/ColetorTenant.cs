using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Coletores;

/// <summary>Tenant autorizado a receber dados de um <see cref="Coletor"/> (AD-005, COL-01).</summary>
public sealed class ColetorTenant : IPertenceAoTenant
{
    public int TenantId { get; set; }
    public int ColetorId { get; set; }
}

internal sealed class ColetorTenantConfiguracao : IEntityTypeConfiguration<ColetorTenant>
{
    public void Configure(EntityTypeBuilder<ColetorTenant> e)
    {
        e.HasKey(x => new { x.TenantId, x.ColetorId });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Coletor>().WithMany().HasForeignKey(x => x.ColetorId).OnDelete(DeleteBehavior.NoAction);
    }
}
