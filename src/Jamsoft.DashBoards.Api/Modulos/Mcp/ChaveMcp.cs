using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Contratos.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Mcp;

/// <summary>
/// Api-key do servidor MCP (Jamsoft.Sge.Mcp): identifica o tenant cujas vendas o conector do Claude enxerga.
/// Só o SHA-256 da chave é gravado; o <see cref="Prefixo"/> localiza o registro (<see cref="FormatoChaveMcp"/>).
/// Pertence ao tenant: sai junto na exclusão dele.
/// </summary>
public sealed class ChaveMcp : IPertenceAoTenant
{
    public const int DescricaoMaxima = 80;

    public int Id { get; set; }
    public Guid IdPublico { get; set; }
    public int TenantId { get; set; }
    public string Prefixo { get; set; } = "";
    public byte[] Hash { get; set; } = [];
    public string Descricao { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    /// <summary>UTC; nula = não expira.</summary>
    public DateTime? ExpiraEm { get; set; }
    public DateTime? RevogadoEm { get; set; }
}

internal sealed class ChaveMcpConfiguracao : IEntityTypeConfiguration<ChaveMcp>
{
    public void Configure(EntityTypeBuilder<ChaveMcp> e)
    {
        e.ToTable(t => t.HasCheckConstraint("CK_CHAVE_MCP_HASH", $"DATALENGTH(HASH) = {FormatoChaveMcp.TamanhoHash}"));
        e.HasKey(x => x.Id);
        e.Property(x => x.IdPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
        e.HasIndex(x => x.IdPublico).IsUnique();
        // Busca do MCP a cada chave nova no cache: pelo prefixo, sem ir à tabela.
        e.HasIndex(x => x.Prefixo).IsUnique()
            .IncludeProperties(x => new { x.Hash, x.TenantId, x.Ativo, x.ExpiraEm });
        e.HasIndex(x => x.TenantId);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.Property(x => x.Prefixo).HasColumnType($"char({FormatoChaveMcp.TamanhoPrefixo})").IsRequired();
        e.Property(x => x.Hash).HasColumnType($"varbinary({FormatoChaveMcp.TamanhoHash})").IsRequired();
        e.Property(x => x.Descricao).HasMaxLength(ChaveMcp.DescricaoMaxima).IsRequired();
        e.Property(x => x.Ativo).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.CriadoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.ExpiraEm).HasColumnType("datetime2(0)");
        e.Property(x => x.RevogadoEm).HasColumnType("datetime2(0)");
    }
}
