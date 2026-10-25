namespace Jamsoft.DashBoards.Contratos.Ingestao;

/// <summary>Uma venda no contrato do spec ("Modelo de dados da venda").</summary>
public sealed record VendaContrato(int VendaId, string TipoDfe, DateTimeOffset DataHora, decimal Valor,
    int? CodCliente, string? NomeCliente, int? VendedorCodigo, string? VendedorNome, string Situacao,
    string NomeEmpresa, int CodEmpresa, int QtdItens);
