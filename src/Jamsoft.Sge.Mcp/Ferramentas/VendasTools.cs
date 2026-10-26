using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Jamsoft.Sge.Mcp.Configuracao;
using Jamsoft.Sge.Mcp.Dados;
using Jamsoft.Sge.Mcp.Seguranca;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Jamsoft.Sge.Mcp.Ferramentas;

/// <summary>
/// Ferramentas MCP de vendas do SGE. O banco usado é sempre o do cliente dono da api-key
/// da requisição: a ferramenta não recebe nome de banco nem connection string.
/// </summary>
[McpServerToolType]
public sealed class VendasTools(
    IHttpContextAccessor http,
    VendasRepositorio vendas,
    IOptions<McpOptions> opcoes,
    ILogger<VendasTools> log)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [McpServerTool(Name = "listar_vendas", Title = "Listar vendas", ReadOnly = true, Idempotent = true)]
    [Description("Lista as notas de saída (NF-e e NFC-e) do SGE no período, uma linha por venda, " +
                 "com tipo de DF-e, data, valor, cliente, vendedor, situação (Finalizada, Cancelada, Inutilizada), " +
                 "empresa/loja e quantidade de itens. O período filtra a data de faturamento. " +
                 "Devolve um array JSON de objetos.")]
    public async Task<string> ListarVendas(
        [Description("Data inicial, inclusive, no formato yyyy-MM-dd. Se vazio: primeiro dia do mês, dois meses atrás.")]
        string? inicio = null,
        [Description("Data final, inclusive, no formato yyyy-MM-dd. Se vazio: hoje.")]
        string? fim = null,
        CancellationToken ct = default)
    {
        var tenant = http.HttpContext.ObterTenant();
        var hoje = DateOnly.FromDateTime(DateTime.Now);

        var dataFim = LerData(fim, nameof(fim)) ?? hoje;
        var dataInicio = LerData(inicio, nameof(inicio)) ?? new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-2);

        if (dataInicio > dataFim)
            throw new McpException("A data inicial é maior que a data final.");

        var dias = dataFim.DayNumber - dataInicio.DayNumber + 1;
        if (dias > opcoes.Value.MaxDiasConsulta)
            throw new McpException($"Período de {dias} dias passa do limite de {opcoes.Value.MaxDiasConsulta} dias por consulta.");

        try
        {
            var linhas = await vendas.ListarAsync(tenant, dataInicio, dataFim, ct);
            log.LogInformation("listar_vendas: cliente {Cliente}, {Inicio:yyyy-MM-dd} a {Fim:yyyy-MM-dd}, {Qtd} linhas.",
                tenant.Cliente, dataInicio, dataFim, linhas.Count);
            return JsonSerializer.Serialize(linhas, Json);
        }
        catch (SqlException ex)
        {
            // O detalhe vai para o log do servidor; para o cliente MCP, só uma mensagem segura.
            log.LogError(ex, "Erro SQL em listar_vendas para o cliente {Cliente}.", tenant.Cliente);
            throw new McpException("Não foi possível consultar o banco de dados do cliente. Veja o log do servidor MCP.");
        }
    }

    [McpServerTool(Name = "identificar_conexao", Title = "Identificar conexão", ReadOnly = true, Idempotent = true)]
    [Description("Mostra para qual cliente, servidor e banco a api-key atual aponta (sem senha). Útil para testar a conexão.")]
    public string IdentificarConexao()
    {
        var tenant = http.HttpContext.ObterTenant();
        var cs = new SqlConnectionStringBuilder(tenant.ConnectionString);
        return JsonSerializer.Serialize(new
        {
            cliente = tenant.Cliente,
            servidor = cs.DataSource,
            banco = cs.InitialCatalog,
        }, Json);
    }

    private static DateOnly? LerData(string? valor, string nome)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        if (DateOnly.TryParseExact(valor.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            return data;
        throw new McpException($"Parâmetro '{nome}' inválido: use o formato yyyy-MM-dd (ex.: 2026-09-01).");
    }
}
