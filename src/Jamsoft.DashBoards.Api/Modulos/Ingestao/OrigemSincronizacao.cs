using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>
/// Uma configuração do Coletor para uma rotina, vista pela plataforma: dona das empresas que envia (ING-10) e
/// guarda a última execução (ING-09).
/// </summary>
public sealed class OrigemSincronizacao : IPertenceAoTenant
{
    public const int RotinaMaxima = 30;
    public const int BancoOrigemMaximo = 256;
    public const int SituacaoMaxima = 20;
    public const int MensagemMaxima = 500;

    public int Id { get; set; }
    public Guid IdPublico { get; set; }
    public int TenantId { get; set; }
    public int ColetorId { get; set; }
    /// <summary>Gerado pelo Coletor, estável por configuração.</summary>
    public Guid ConfiguracaoId { get; set; }
    public string Rotina { get; set; } = "";
    /// <summary>Só exibição (servidor/banco), sem credenciais.</summary>
    public string? BancoOrigem { get; set; }
    /// <summary>UTC.</summary>
    public DateTime? UltimaExecucaoInicio { get; set; }
    /// <summary>UTC.</summary>
    public DateTime? UltimaExecucaoFim { get; set; }
    public string? UltimaSituacao { get; set; }
    public string? UltimaMensagem { get; set; }
    public int? RegistrosUltima { get; set; }
    /// <summary>UTC.</summary>
    public DateTime? UltimoSucessoEm { get; set; }
}

internal sealed class OrigemSincronizacaoConfiguracao : IEntityTypeConfiguration<OrigemSincronizacao>
{
    public void Configure(EntityTypeBuilder<OrigemSincronizacao> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.IdPublico).HasDefaultValueSql("NEWSEQUENTIALID()");
        e.HasIndex(x => x.IdPublico).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.ConfiguracaoId, x.Rotina }).IsUnique();
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Coletor>().WithMany().HasForeignKey(x => x.ColetorId).OnDelete(DeleteBehavior.NoAction);
        e.Property(x => x.Rotina).HasColumnType($"varchar({OrigemSincronizacao.RotinaMaxima})").IsRequired();
        e.Property(x => x.BancoOrigem).HasMaxLength(OrigemSincronizacao.BancoOrigemMaximo);
        e.Property(x => x.UltimaExecucaoInicio).HasColumnType("datetime2(0)");
        e.Property(x => x.UltimaExecucaoFim).HasColumnType("datetime2(0)");
        e.Property(x => x.UltimaSituacao).HasColumnType($"varchar({OrigemSincronizacao.SituacaoMaxima})");
        e.Property(x => x.UltimaMensagem).HasMaxLength(OrigemSincronizacao.MensagemMaxima);
        e.Property(x => x.UltimoSucessoEm).HasColumnType("datetime2(0)");
    }
}
