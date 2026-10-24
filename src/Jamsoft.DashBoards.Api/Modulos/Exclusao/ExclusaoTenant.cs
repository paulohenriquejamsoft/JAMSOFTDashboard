using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Exclusao;

public enum SituacaoExclusao
{
    Pendente,
    EmExecucao,
    Concluida,
    Falhou,
}

/// <summary>
/// Job de exclusão total de um tenant (§5.4). Sem FK para TENANT: o tenant é removido ao final
/// e o job continua como registro do que foi feito.
/// </summary>
public sealed class ExclusaoTenant
{
    public int Id { get; set; }
    public Guid IdPublico { get; set; }
    public int TenantId { get; set; }
    public Guid TenantIdPublico { get; set; }
    public string TenantNome { get; set; } = "";
    public SituacaoExclusao Situacao { get; set; }
    /// <summary>Tabela em processamento; a retomada recomeça daqui.</summary>
    public string? EtapaAtual { get; set; }
    /// <summary>JSON { "TABELA": linhas }.</summary>
    public string? LinhasRemovidas { get; set; }
    public string? Erro { get; set; }
    public Guid SolicitadoPorIdPublico { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? IniciadoEm { get; set; }
    public DateTime? HeartbeatEm { get; set; }
    public DateTime? ConcluidoEm { get; set; }
}

internal sealed class ExclusaoTenantConfiguracao : IEntityTypeConfiguration<ExclusaoTenant>
{
    public void Configure(EntityTypeBuilder<ExclusaoTenant> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.IdPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
        e.HasIndex(x => x.IdPublico).IsUnique();
        // Um único job ativo por tenant (DEL-09).
        e.HasIndex(x => x.TenantId).IsUnique().HasFilter("[SITUACAO] IN ('Pendente','EmExecucao')");
        e.Property(x => x.TenantNome).HasMaxLength(120).IsRequired();
        e.Property(x => x.Situacao).HasConversion<string>().HasColumnType("varchar(20)");
        e.Property(x => x.EtapaAtual).HasColumnType("varchar(128)");
        e.Property(x => x.Erro).HasMaxLength(2000);
        e.Property(x => x.CriadoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.IniciadoEm).HasColumnType("datetime2(0)");
        e.Property(x => x.HeartbeatEm).HasColumnType("datetime2(0)");
        e.Property(x => x.ConcluidoEm).HasColumnType("datetime2(0)");
    }
}
