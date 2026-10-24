using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Auditoria;

/// <summary>
/// Operação crítica registrada (AUD-01/AUD-02). Somente inserção. Sem FK para TENANT: guarda o
/// ID público e o nome do tenant como texto para sobreviver à exclusão do tenant.
/// </summary>
public sealed class RegistroAuditoria
{
    public long Id { get; set; }
    /// <summary>UTC.</summary>
    public DateTime OcorridoEm { get; set; }
    public Guid? UsuarioIdPublico { get; set; }
    public string? UsuarioEmail { get; set; }
    public string? Perfil { get; set; }
    public string Operacao { get; set; } = "";
    public Guid? RecursoIdPublico { get; set; }
    public Guid? TenantIdPublico { get; set; }
    public string? TenantNome { get; set; }
    public string? Ip { get; set; }
    public string Resultado { get; set; } = "";
    /// <summary>JSON sem senhas, tokens ou segredos.</summary>
    public string? Detalhes { get; set; }
}

internal sealed class RegistroAuditoriaConfiguracao : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> e)
    {
        e.ToTable("AUDITORIA");
        e.HasKey(x => x.Id);
        e.Property(x => x.OcorridoEm).HasColumnType("datetime2(3)");
        e.HasIndex(x => x.OcorridoEm);
        e.HasIndex(x => new { x.TenantIdPublico, x.OcorridoEm });
        e.Property(x => x.UsuarioEmail).HasMaxLength(254);
        e.Property(x => x.Perfil).HasColumnType("varchar(20)");
        e.Property(x => x.Operacao).HasColumnType("varchar(60)").IsRequired();
        e.Property(x => x.TenantNome).HasMaxLength(120);
        e.Property(x => x.Ip).HasColumnType("varchar(45)");
        e.Property(x => x.Resultado).HasColumnType("varchar(20)").IsRequired();
        e.Property(x => x.Detalhes).HasMaxLength(2000);
    }
}
