using System.Text.Json;
using Jamsoft.DashBoards.Contratos.Ingestao;

namespace Jamsoft.DashBoards.Coletor.Testes.Unidade.Contratos;

/// <summary>Nomes do contrato da ingestão (design, "Ingestão de lotes") e limites do lote (ING-08).</summary>
[Trait("Categoria", "Unidade")]
public sealed class LoteVendasTestes
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static LoteVendas Lote() => new(
        BatchId: Guid.Parse("6f1c2a8e-0d2b-4d6f-9a51-3f2e7c1b9d40"),
        Tenant: Guid.Parse("b3e1d2c4-5a6f-4b7c-8d9e-0f1a2b3c4d5e"),
        ConfiguracaoId: Guid.Parse("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"),
        Rotina: "Vendas",
        BancoOrigem: "SRV01/SGE_LOJA",
        Vendas:
        [
            new VendaContrato(1, "NFC-e", new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.FromHours(-3)), 123.45m,
                10, "Cliente Um", 3, "Vendedor Três", "Contingência", "Loja Centro", 1, 4),
            new VendaContrato(2, "NF-e", new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.FromHours(-3)), 0m,
                null, null, null, null, "Cancelada", "Loja Centro", 1, 0),
        ]);

    [Fact]
    public void Serializado_com_JsonSerializerDefaults_Web_usa_exatamente_os_nomes_do_contrato()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Lote(), Web));
        var raiz = json.RootElement;
        var venda = raiz.GetProperty("vendas")[0];

        Assert.Equal(["batchId", "tenant", "configuracaoId", "rotina", "bancoOrigem", "vendas"],
            raiz.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ["vendaId", "tipoDfe", "dataHora", "valor", "codCliente", "nomeCliente", "vendedorCodigo", "vendedorNome",
             "situacao", "nomeEmpresa", "codEmpresa", "qtdItens"],
            venda.EnumerateObject().Select(p => p.Name));
        Assert.Equal("6f1c2a8e-0d2b-4d6f-9a51-3f2e7c1b9d40", raiz.GetProperty("batchId").GetString());
        Assert.Equal("2026-10-20T14:03:00-03:00", venda.GetProperty("dataHora").GetString());
        Assert.Equal(123.45m, venda.GetProperty("valor").GetDecimal());
        Assert.Equal("Contingência", venda.GetProperty("situacao").GetString());
    }

    [Fact]
    public void Lote_serializado_e_lido_de_volta_fica_igual()
    {
        var original = Lote();

        var lido = JsonSerializer.Deserialize<LoteVendas>(JsonSerializer.Serialize(original, Web), Web)!;

        // Record compara a lista por referência: as vendas são comparadas item a item.
        Assert.Equal(original.Vendas, lido.Vendas);
        Assert.Equal(original with { Vendas = lido.Vendas }, lido);
    }

    [Fact]
    public void Limites_do_lote_sao_500_vendas_e_2_MB()
    {
        Assert.Equal(500, Limites.VendasPorLote);
        Assert.Equal(2 * 1024 * 1024, Limites.BytesPorLote);
    }
}
