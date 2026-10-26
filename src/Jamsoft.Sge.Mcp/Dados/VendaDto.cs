using System.Text.Json.Serialization;

namespace Jamsoft.Sge.Mcp.Dados;

/// <summary>
/// Uma venda (nota de saída). Os nomes no JSON são os mesmos do Resultados.json,
/// que é o formato que o painel de vendas já lê.
/// O Dapper mapeia as colunas com underline (venda_id → VendaId) porque o Program.cs liga
/// DefaultTypeMap.MatchNamesWithUnderscores.
/// </summary>
public sealed class VendaDto
{
    [JsonPropertyName("venda_id")] public long VendaId { get; set; }
    [JsonPropertyName("TipoDFe")] public string TipoDFe { get; set; } = "";
    [JsonPropertyName("data_hora")] public DateTime? DataHora { get; set; }
    [JsonPropertyName("valor")] public decimal Valor { get; set; }
    [JsonPropertyName("cod_cliente")] public long? CodCliente { get; set; }
    [JsonPropertyName("nome_cliente")] public string? NomeCliente { get; set; }
    [JsonPropertyName("vendedor_codigo")] public long? VendedorCodigo { get; set; }
    [JsonPropertyName("vendedor_nome")] public string? VendedorNome { get; set; }
    [JsonPropertyName("situacao")] public string Situacao { get; set; } = "";
    [JsonPropertyName("Nome_Empresa")] public string? NomeEmpresa { get; set; }
    [JsonPropertyName("Cod_Empresa")] public long? CodEmpresa { get; set; }
    [JsonPropertyName("QtdItens")] public int QtdItens { get; set; }
}
