using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Empresas;

/// <summary>Empresa (filial) do tenant, identificada pelo código do ERP (EMP-01).</summary>
public sealed class Empresa : IPertenceAoTenant
{
    public const int NomeMaximo = 80;

    public int Id { get; set; }
    public Guid IdPublico { get; set; }
    public int TenantId { get; set; }
    public int CodEmpresa { get; set; }
    public string Nome { get; set; } = "";
    /// <summary>Nome editado por um usuário: a ingestão não sobrescreve (EMP-05).</summary>
    public bool NomeManual { get; set; }
    public DateTime CriadoEm { get; set; }
    public byte[] Versao { get; set; } = [];
}

internal sealed class EmpresaConfiguracao : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.IdPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
        e.HasIndex(x => x.IdPublico).IsUnique();
        // TENANT_ID na frente: atende ao filtro por tenant e garante o código único por tenant (ISO-05).
        e.HasIndex(x => new { x.TenantId, x.CodEmpresa }).IsUnique();
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.Property(x => x.Nome).HasMaxLength(Empresa.NomeMaximo).IsRequired();
        e.Property(x => x.NomeManual).HasDefaultValue(false).ValueGeneratedNever();
        e.Property(x => x.CriadoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.Versao).IsRowVersion();
    }
}
