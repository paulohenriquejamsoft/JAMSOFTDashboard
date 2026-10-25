using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Coletores;

/// <summary>
/// Credencial de uma instalação do Coletor (AD-005). Só o SHA-256 do <c>client_secret</c> é gravado (COL-02).
/// Fora do filtro por tenant: atende vários tenants, listados em <see cref="ColetorTenant"/>.
/// </summary>
public sealed class Coletor
{
    public const int NomeMaximo = 80;
    public const int TamanhoHash = 32;

    public int Id { get; set; }
    public Guid IdPublico { get; set; }
    public string Nome { get; set; } = "";
    public Guid ClientId { get; set; }
    public byte[] SecretHash { get; set; } = [];
    public bool Ativo { get; set; } = true;
    /// <summary>Incrementada a cada rotação, revogação ou troca de tenants; vai no token como <c>cred_ver</c>.</summary>
    public int VersaoCredencial { get; set; } = 1;
    public DateTime CriadoEm { get; set; }
    public DateTime? RevogadoEm { get; set; }
    public DateTime? UltimoTokenEm { get; set; }
    public byte[] Versao { get; set; } = [];
}

internal sealed class ColetorConfiguracao : IEntityTypeConfiguration<Coletor>
{
    public void Configure(EntityTypeBuilder<Coletor> e)
    {
        // SPEC_DEVIATION: SECRET_HASH é VARBINARY(32) com CHECK de 32 bytes, e não BINARY(32) como no design.
        // Reason: BINARY(32) completa com zeros um valor menor sem erro; o CHECK garante que só um hash de 32 bytes entra.
        e.ToTable(t => t.HasCheckConstraint("CK_COLETOR_SECRET_HASH", $"DATALENGTH(SECRET_HASH) = {Coletor.TamanhoHash}"));
        e.HasKey(x => x.Id);
        e.Property(x => x.IdPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
        e.HasIndex(x => x.IdPublico).IsUnique();
        e.Property(x => x.Nome).HasMaxLength(Coletor.NomeMaximo).IsRequired();
        e.HasIndex(x => x.ClientId).IsUnique();
        e.Property(x => x.SecretHash).HasColumnType($"varbinary({Coletor.TamanhoHash})").IsRequired();
        e.Property(x => x.Ativo).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.VersaoCredencial).HasDefaultValue(1).ValueGeneratedNever();
        e.Property(x => x.CriadoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.RevogadoEm).HasColumnType("datetime2(0)");
        e.Property(x => x.UltimoTokenEm).HasColumnType("datetime2(0)");
        e.Property(x => x.Versao).IsRowVersion();
    }
}
