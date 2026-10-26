using System.Text.Json.Serialization;

namespace Jamsoft.Sge.Mcp.Dados;

/// <summary>
/// Uma venda da tabela VENDA da plataforma (enviada pelo Coletor). Os nomes no JSON são os mesmos do
/// Resultados.json, que é o formato que o painel de vendas já lê.
/// </summary>
public sealed class VendaDto
{
    [JsonPropertyName("venda_id")] public int VendaId { get; set; }
    [JsonPropertyName("TipoDFe")] public string TipoDfe { get; set; } = "";
    /// <summary>No fuso de São Paulo.</summary>
    [JsonPropertyName("data_hora")] public DateTimeOffset DataHora { get; set; }
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
    [JsonPropertyName("cod_cliente")] public int? CodCliente { get; set; }
    [JsonPropertyName("nome_cliente")] public string? NomeCliente { get; set; }
    [JsonPropertyName("vendedor_codigo")] public int? VendedorCodigo { get; set; }
    [JsonPropertyName("vendedor_nome")] public string? VendedorNome { get; set; }
    [JsonPropertyName("situacao")] public string Situacao { get; set; } = "";
    [JsonPropertyName("Nome_Empresa")] public string NomeEmpresa { get; set; } = "";
    [JsonPropertyName("Cod_Empresa")] public int CodEmpresa { get; set; }
    [JsonPropertyName("QtdItens")] public int QtdItens { get; set; }
}
