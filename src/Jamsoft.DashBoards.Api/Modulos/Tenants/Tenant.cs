using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Tenants;

/// <summary>Cliente da plataforma. Exposto só pelo <see cref="IdPublico"/> (TEN-07).</summary>
public sealed class Tenant
{
    public int Id { get; set; }
    public Guid IdPublico { get; set; }
    public string Nome { get; set; } = "";
    /// <summary>Só dígitos (14).</summary>
    public string Cnpj { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime? AlteradoEm { get; set; }
    public byte[] Versao { get; set; } = [];
}

internal sealed class TenantConfiguracao : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> e)
    {
        e.HasKey(t => t.Id);
        e.Property(t => t.IdPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
        e.HasIndex(t => t.IdPublico).IsUnique();
        e.Property(t => t.Nome).HasMaxLength(120).IsRequired();
        e.Property(t => t.Cnpj).HasColumnType("char(14)").IsRequired();
        e.HasIndex(t => t.Cnpj).IsUnique();
        // DEFAULT 1 vale para inserções diretas no banco; o EF sempre envia o valor (false não vira true).
        e.Property(t => t.Ativo).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(t => t.CriadoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(t => t.AlteradoEm).HasColumnType("datetime2(0)");
        e.Property(t => t.Versao).IsRowVersion();
    }
}
