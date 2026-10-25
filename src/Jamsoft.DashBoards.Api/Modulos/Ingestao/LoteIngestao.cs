using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>
/// Lote já processado, com a resposta enviada ao Coletor: um reenvio do mesmo <see cref="BatchId"/> no tenant
/// recebe a mesma resposta sem regravar as vendas (ING-05, AD-014).
/// </summary>
public sealed class LoteIngestao : IPertenceAoTenant
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid BatchId { get; set; }
    public int OrigemId { get; set; }
    /// <summary>UTC; usado pela retenção dos lotes.</summary>
    public DateTime RecebidoEm { get; set; }
    public int Inseridos { get; set; }
    public int Atualizados { get; set; }
    public int Rejeitados { get; set; }
    /// <summary>JSON da resposta devolvida ao Coletor.</summary>
    public string Resposta { get; set; } = "";
}

internal sealed class LoteIngestaoConfiguracao : IEntityTypeConfiguration<LoteIngestao>
{
    public void Configure(EntityTypeBuilder<LoteIngestao> e)
    {
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.TenantId, x.BatchId }).IsUnique();
        e.HasIndex(x => x.RecebidoEm);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<OrigemSincronizacao>().WithMany().HasForeignKey(x => x.OrigemId).OnDelete(DeleteBehavior.NoAction);
        e.Property(x => x.RecebidoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.Resposta).HasColumnType("nvarchar(max)").IsRequired();
    }
}
