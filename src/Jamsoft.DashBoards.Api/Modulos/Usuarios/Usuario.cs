using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Usuarios;

/// <summary>
/// Usuário do portal (AD-010). SuperAdmin não tem tenant; Admin e Consulta pertencem a exatamente um.
/// Fora do filtro global por tenant: o login acontece antes de haver tenant no contexto.
/// </summary>
public sealed class Usuario
{
    public int Id { get; set; }
    public Guid IdPublico { get; set; }
    public int? TenantId { get; set; }
    public string Nome { get; set; } = "";
    /// <summary>Normalizado em minúsculas; único na plataforma.</summary>
    public string Email { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Perfil Perfil { get; set; }
    public bool Ativo { get; set; } = true;
    public bool TrocarSenha { get; set; } = true;
    public byte FalhasLogin { get; set; }
    public DateTime? BloqueadoAte { get; set; }
    public DateTime? UltimoLoginEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public byte[] Versao { get; set; } = [];
}

internal sealed class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> e)
    {
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_USUARIO_PERFIL", "PERFIL IN ('SuperAdmin','Admin','Consulta')");
            t.HasCheckConstraint("CK_USUARIO_PERFIL_TENANT",
                "(PERFIL = 'SuperAdmin' AND TENANT_ID IS NULL) OR (PERFIL <> 'SuperAdmin' AND TENANT_ID IS NOT NULL)");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.IdPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
        e.HasIndex(x => x.IdPublico).IsUnique();
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.HasIndex(x => new { x.TenantId, x.Perfil, x.Ativo });
        e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        e.Property(x => x.Email).HasMaxLength(254).IsRequired();
        e.HasIndex(x => x.Email).IsUnique();
        e.Property(x => x.SenhaHash).HasMaxLength(200).IsRequired();
        e.Property(x => x.Perfil).HasConversion<string>().HasColumnType("varchar(20)");
        e.Property(x => x.Ativo).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.TrocarSenha).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.FalhasLogin).HasDefaultValue((byte)0).ValueGeneratedNever();
        e.Property(x => x.BloqueadoAte).HasColumnType("datetime2(0)");
        e.Property(x => x.UltimoLoginEm).HasColumnType("datetime2(0)");
        e.Property(x => x.CriadoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.Versao).IsRowVersion();
    }
}
