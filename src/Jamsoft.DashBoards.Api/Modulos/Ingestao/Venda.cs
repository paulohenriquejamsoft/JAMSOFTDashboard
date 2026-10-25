using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>
/// Venda recebida do Coletor, única pela chave de negócio (TENANT_ID, COD_EMPRESA, VENDA_ID) (AD-006, ING-04).
/// Gravada pelo MERGE do <c>GravadorVendas</c>.
/// </summary>
public sealed class Venda : IPertenceAoTenant
{
    public const int NomeClienteMaximo = 120;
    public const int VendedorNomeMaximo = 80;

    /// <summary>Domínio de <see cref="Situacao"/> (contrato da venda).</summary>
    public static readonly IReadOnlyList<string> Situacoes = ["Finalizada", "Cancelada", "Inutilizada", "Contingência"];

    /// <summary>Domínio de <see cref="TipoDfe"/> (contrato da venda).</summary>
    public static readonly IReadOnlyList<string> TiposDfe = ["NF-e", "NFC-e"];

    public long Id { get; set; }
    public int TenantId { get; set; }
    public int EmpresaId { get; set; }
    public int CodEmpresa { get; set; }
    public int VendaId { get; set; }
    public string TipoDfe { get; set; } = "";
    public DateTimeOffset DataHora { get; set; }
    public decimal Valor { get; set; }
    public int? CodCliente { get; set; }
    public string? NomeCliente { get; set; }
    public int? VendedorCodigo { get; set; }
    public string? VendedorNome { get; set; }
    public string Situacao { get; set; } = "";
    public int QtdItens { get; set; }
    public int OrigemId { get; set; }
    /// <summary>UTC.</summary>
    public DateTime RecebidoEm { get; set; }
    /// <summary>UTC; preenchido quando um reenvio altera a venda.</summary>
    public DateTime? AlteradoEm { get; set; }
}

internal sealed class VendaConfiguracao : IEntityTypeConfiguration<Venda>
{
    public void Configure(EntityTypeBuilder<Venda> e)
    {
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_VENDA_SITUACAO", $"SITUACAO IN ({Lista(Venda.Situacoes, "N")})");
            t.HasCheckConstraint("CK_VENDA_TIPO_DFE", $"TIPO_DFE IN ({Lista(Venda.TiposDfe, "")})");
            t.HasCheckConstraint("CK_VENDA_VALOR", "VALOR >= 0");
            t.HasCheckConstraint("CK_VENDA_QTD_ITENS", "QTD_ITENS >= 0");
        });
        e.HasKey(x => x.Id);
        // TENANT_ID na frente: chave do upsert (AD-006) e filtro por tenant.
        e.HasIndex(x => new { x.TenantId, x.CodEmpresa, x.VendaId }).IsUnique();
        // Consultas do painel-vendas por período, sem ir à tabela.
        e.HasIndex(x => new { x.TenantId, x.DataHora })
            .IncludeProperties(x => new { x.EmpresaId, x.Valor, x.Situacao, x.TipoDfe, x.QtdItens, x.VendedorCodigo, x.CodCliente });
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.NoAction);
        e.HasOne<OrigemSincronizacao>().WithMany().HasForeignKey(x => x.OrigemId).OnDelete(DeleteBehavior.NoAction);
        e.Property(x => x.TipoDfe).HasColumnType("varchar(6)").IsRequired();
        e.Property(x => x.DataHora).HasColumnType("datetimeoffset(0)");
        e.Property(x => x.Valor).HasColumnType("decimal(19,4)");
        e.Property(x => x.NomeCliente).HasMaxLength(Venda.NomeClienteMaximo);
        e.Property(x => x.VendedorNome).HasMaxLength(Venda.VendedorNomeMaximo);
        e.Property(x => x.Situacao).HasMaxLength(12).IsRequired();
        e.Property(x => x.RecebidoEm).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.AlteradoEm).HasColumnType("datetime2(0)");
    }

    /// <summary>Valores entre aspas para o IN do CHECK; <paramref name="prefixo"/> "N" para nvarchar.</summary>
    private static string Lista(IEnumerable<string> valores, string prefixo) =>
        string.Join(",", valores.Select(v => $"{prefixo}'{v}'"));
}
