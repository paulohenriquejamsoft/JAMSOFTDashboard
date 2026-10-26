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
/// Ferramentas MCP de vendas. O tenant é sempre o dono da api-key da requisição:
/// a ferramenta não recebe tenant, empresa de outro tenant nem connection string.
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
    [Description("Lista as vendas (NF-e, NFC-e e Pedido) do cliente no período, uma linha por venda, " +
                 "com tipo de documento, data/hora (fuso de São Paulo), valor, cliente, vendedor, " +
                 "situação (Finalizada, Contingência, Cancelada, Inutilizada), empresa/loja e quantidade de itens. " +
                 "Vendas efetivadas = Finalizada + Contingência. O período filtra a data/hora da venda. " +
                 "Devolve um array JSON de objetos.")]
    public async Task<string> ListarVendas(
        [Description("Data inicial, inclusive, no formato yyyy-MM-dd. Se vazio: primeiro dia do mês, dois meses atrás.")]
        string? inicio = null,
        [Description("Data final, inclusive, no formato yyyy-MM-dd. Se vazio: hoje.")]
        string? fim = null,
        CancellationToken ct = default)
    {
        var tenant = http.HttpContext.ObterTenant();
        var hoje = VendasRepositorio.HojeEmSaoPaulo();

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
            // Nunca registra dados de venda: só o tenant, o período e a quantidade.
            log.LogInformation("listar_vendas: tenant {Tenant}, {Inicio:yyyy-MM-dd} a {Fim:yyyy-MM-dd}, {Qtd} linhas.",
                tenant.IdPublico, dataInicio, dataFim, linhas.Count);
            return JsonSerializer.Serialize(linhas, Json);
        }
        catch (SqlException ex)
        {
            // O detalhe vai para o log do servidor; para o cliente MCP, só uma mensagem segura.
            log.LogError(ex, "Erro SQL em listar_vendas para o tenant {Tenant}.", tenant.IdPublico);
            throw new McpException("Não foi possível consultar as vendas. Veja o log do servidor MCP.");
        }
    }

    [McpServerTool(Name = "identificar_tenant", Title = "Identificar tenant", ReadOnly = true, Idempotent = true)]
    [Description("Mostra de qual cliente (tenant) são os dados que a api-key atual enxerga. Útil para testar a conexão.")]
    public string IdentificarTenant()
    {
        var tenant = http.HttpContext.ObterTenant();
        return JsonSerializer.Serialize(new
        {
            tenant = tenant.Nome,
            tenantId = tenant.IdPublico,
            chave = tenant.PrefixoChave,
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
