using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Contratos.Ingestao;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>Vendas do lote separadas em válidas (já truncadas) e rejeitadas com o motivo (ING-06).</summary>
public sealed record ResultadoValidacao(IReadOnlyList<VendaContrato> Validas, IReadOnlyList<VendaRejeitada> Rejeitadas);

/// <summary>
/// Confere cada venda contra o contrato do spec ("Modelo de dados da venda"): obrigatórios, <c>vendaId</c> e
/// <c>codEmpresa</c> positivos, <c>valor</c> e <c>qtdItens</c> não negativos, domínio de <c>situacao</c> e
/// <c>tipoDfe</c>. Textos longos demais são truncados, não rejeitados (edge case do ING-06). Uma venda inválida
/// não derruba as demais.
/// </summary>
public static class ValidacaoVenda
{
    public const string MotivoAusente = "Venda ausente";
    public const string MotivoVendaId = "vendaId deve ser maior que zero";
    public const string MotivoCodEmpresa = "codEmpresa deve ser maior que zero";
    public const string MotivoValor = "valor não pode ser negativo";
    public const string MotivoQtdItens = "qtdItens não pode ser negativo";
    public const string MotivoDataHora = "dataHora é obrigatória";
    public const string MotivoNomeEmpresa = "nomeEmpresa é obrigatório";
    public const string MotivoRepetida = "Venda repetida no lote";
    public static readonly string MotivoSituacao = $"situacao deve ser {string.Join(", ", Venda.Situacoes)}";
    public static readonly string MotivoTipoDfe = $"tipoDfe deve ser {string.Join(" ou ", Venda.TiposDfe)}";

    public const string SeparadorMotivos = "; ";

    public static ResultadoValidacao Validar(IEnumerable<VendaContrato?> vendas)
    {
        var validas = new List<VendaContrato>();
        var rejeitadas = new List<VendaRejeitada>();
        var chaves = new HashSet<(int CodEmpresa, int VendaId)>();

        foreach (var venda in vendas)
        {
            if (venda is null)
            {
                rejeitadas.Add(new VendaRejeitada(0, 0, MotivoAusente));
                continue;
            }

            var motivos = Motivos(venda);
            // A mesma chave duas vezes no lote faria o MERGE tocar a mesma linha duas vezes: vale a primeira.
            if (motivos.Count == 0 && !chaves.Add((venda.CodEmpresa, venda.VendaId))) motivos.Add(MotivoRepetida);

            if (motivos.Count > 0)
                rejeitadas.Add(new VendaRejeitada(venda.VendaId, venda.CodEmpresa, string.Join(SeparadorMotivos, motivos)));
            else
                validas.Add(Truncar(venda));
        }
        return new ResultadoValidacao(validas, rejeitadas);
    }

    private static List<string> Motivos(VendaContrato venda)
    {
        var motivos = new List<string>();
        if (venda.VendaId <= 0) motivos.Add(MotivoVendaId);
        if (venda.CodEmpresa <= 0) motivos.Add(MotivoCodEmpresa);
        if (venda.DataHora == default) motivos.Add(MotivoDataHora);
        if (venda.Valor < 0) motivos.Add(MotivoValor);
        if (venda.QtdItens < 0) motivos.Add(MotivoQtdItens);
        if (!Venda.Situacoes.Contains(venda.Situacao, StringComparer.Ordinal)) motivos.Add(MotivoSituacao);
        if (!Venda.TiposDfe.Contains(venda.TipoDfe, StringComparer.Ordinal)) motivos.Add(MotivoTipoDfe);
        if (string.IsNullOrWhiteSpace(venda.NomeEmpresa)) motivos.Add(MotivoNomeEmpresa);
        return motivos;
    }

    private static VendaContrato Truncar(VendaContrato venda) => venda with
    {
        NomeCliente = Cortar(venda.NomeCliente, Venda.NomeClienteMaximo),
        VendedorNome = Cortar(venda.VendedorNome, Venda.VendedorNomeMaximo),
        NomeEmpresa = Cortar(venda.NomeEmpresa, Empresa.NomeMaximo)!,
    };

    private static string? Cortar(string? texto, int maximo) =>
        texto is not null && texto.Length > maximo ? texto[..maximo] : texto;
}
