using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

/// <summary>
/// Refresh token rotativo (AD-010). Só o SHA-256 é gravado. Todos os tokens de um mesmo login
/// compartilham a <see cref="Familia"/>, usada para revogar a sessão inteira.
/// </summary>
public sealed class RefreshToken
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    /// <summary>Cópia do tenant do usuário, para a rotina de exclusão de tenant.</summary>
    public int? TenantId { get; set; }
    public byte[] Hash { get; set; } = [];
    public Guid Familia { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; set; }
    public DateTime? RevogadoEm { get; set; }
}

internal sealed class RefreshTokenConfiguracao : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.HasKey(x => x.Id);
        e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.NoAction);
        e.HasIndex(x => x.UsuarioId);
        e.HasIndex(x => x.TenantId);
        e.Property(x => x.Hash).HasColumnType("binary(32)").IsRequired();
        e.HasIndex(x => x.Hash).IsUnique();
        e.Property(x => x.CriadoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.ExpiraEm).HasColumnType("datetime2(0)");
        e.Property(x => x.UsadoEm).HasColumnType("datetime2(0)");
        e.Property(x => x.RevogadoEm).HasColumnType("datetime2(0)");
    }
}
