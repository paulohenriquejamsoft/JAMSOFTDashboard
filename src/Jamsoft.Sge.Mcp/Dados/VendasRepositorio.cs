using System.Data;
using Dapper;
using Jamsoft.Sge.Mcp.Configuracao;
using Jamsoft.Sge.Mcp.Seguranca;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Jamsoft.Sge.Mcp.Dados;

/// <summary>
/// Vendas do tenant na tabela VENDA do banco da plataforma (ConnectionStrings:Plataforma). O filtro por TENANT_ID
/// é sempre o do tenant da api-key. As datas são inclusivas no fuso de São Paulo, como no painel da API (AD-018).
/// </summary>
public sealed class VendasRepositorio(IConfiguration configuracao, IOptions<McpOptions> opcoes)
{
    /// <summary>America/Sao_Paulo ("E. South America Standard Time" no Windows).</summary>
    public static readonly TimeZoneInfo FusoSaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly string _conexao = configuracao.GetConnectionString("Plataforma")
        ?? throw new InvalidOperationException("ConnectionStrings:Plataforma não configurada.");

    // Usa o índice (TENANT_ID, DATA_HORA) da VENDA. Empresa: EMPRESA.NOME, que guarda o nome manual.
    private const string SqlVendas = """
        SELECT
            v.VENDA_ID                                                 AS VendaId,
            v.TIPO_DFE                                                 AS TipoDfe,
            v.DATA_HORA AT TIME ZONE 'E. South America Standard Time'  AS DataHora,
            v.VALOR                                                    AS Valor,
            v.COD_CLIENTE                                              AS CodCliente,
            v.NOME_CLIENTE                                             AS NomeCliente,
            v.VENDEDOR_CODIGO                                          AS VendedorCodigo,
            v.VENDEDOR_NOME                                            AS VendedorNome,
            v.SITUACAO                                                 AS Situacao,
            e.NOME                                                     AS NomeEmpresa,
            v.COD_EMPRESA                                              AS CodEmpresa,
            v.QTD_ITENS                                                AS QtdItens
        FROM dbo.VENDA   AS v
        JOIN dbo.EMPRESA AS e ON e.ID = v.EMPRESA_ID AND e.TENANT_ID = v.TENANT_ID
        WHERE v.TENANT_ID = @TenantId
          AND v.DATA_HORA >= @Desde
          AND v.DATA_HORA <  @AteExclusivo
        ORDER BY v.DATA_HORA, v.ID
        """;

    public async Task<IReadOnlyList<VendaDto>> ListarAsync(Tenant tenant, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var parametros = new DynamicParameters();
        parametros.Add("TenantId", tenant.Id, DbType.Int32);
        // DATA_HORA é datetimeoffset(0): parâmetro do mesmo tipo, sem conversão na coluna.
        parametros.Add("Desde", MeiaNoiteUtc(inicio), DbType.DateTimeOffset);
        parametros.Add("AteExclusivo", MeiaNoiteUtc(fim.AddDays(1)), DbType.DateTimeOffset);

        await using var conexao = new SqlConnection(_conexao);
        var linhas = await conexao.QueryAsync<VendaDto>(new CommandDefinition(
            SqlVendas,
            parametros,
            commandTimeout: opcoes.Value.TimeoutConsultaSegundos,
            cancellationToken: ct));

        return linhas.AsList();
    }

    /// <summary>Data de hoje em São Paulo.</summary>
    public static DateOnly HojeEmSaoPaulo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, FusoSaoPaulo).DateTime);

    private static DateTimeOffset MeiaNoiteUtc(DateOnly data)
    {
        var local = data.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, FusoSaoPaulo.GetUtcOffset(local)).ToUniversalTime();
    }
}
