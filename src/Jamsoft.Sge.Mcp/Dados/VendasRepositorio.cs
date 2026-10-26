using System.Data;
using Dapper;
using Jamsoft.Sge.Mcp.Configuracao;
using Jamsoft.Sge.Mcp.Seguranca;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Jamsoft.Sge.Mcp.Dados;

public sealed class VendasRepositorio(IOptions<McpOptions> opcoes)
{
    // Mesma consulta enviada, com duas mudanças:
    //  - o filtro fixo MONTH(datafatura) >= 9 AND YEAR(datafatura) = 2026 virou um intervalo
    //    parametrizado (>= @Inicio e < @FimExclusivo), que também deixa o SQL Server usar índice em datafatura;
    //  - os CASE aninhados de situacao viraram um CASE só, com o mesmo resultado.
    private const string SqlVendas = """
        SELECT
            ns.NumNotaSaida                                         AS venda_id,
            CASE WHEN ns.modelo = 65 THEN 'NFC-e' ELSE 'NF-e' END   AS TipoDFe,
            ns.dataentrega                                          AS data_hora,
            ns.totnota                                              AS valor,
            ns.codcliente                                           AS cod_cliente,
            ns.FANTASIA                                             AS nome_cliente,
            ns.codvendedor                                          AS vendedor_codigo,
            ns.nomevend                                             AS vendedor_nome,
            CASE
                WHEN ns.SITUACAONOTA IN (5, 6) THEN 'Finalizada'
                WHEN ns.SITUACAONOTA = 9       THEN 'Cancelada'
                ELSE 'Inutilizada'
            END                                                     AS situacao,
            ns.Empresa                                              AS Nome_Empresa,
            ns.codEmpresa                                           AS Cod_Empresa,
            (SELECT COUNT(*)
               FROM NotaSaidaProd AS p
              WHERE p.numnotasaida = ns.numnotasaida)               AS QtdItens
        FROM c_NotaSaida AS ns
        WHERE ns.situacaonota >= 5
          AND ns.TotalCondNeutra = 0
          AND ns.datafatura >= @Inicio
          AND ns.datafatura <  @FimExclusivo
        ORDER BY ns.numnotasaida ASC
        """;

    public async Task<IReadOnlyList<VendaDto>> ListarAsync(Tenant tenant, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var parametros = new DynamicParameters();
        // DbType.DateTime casa com colunas datetime e evita conversão implícita na coluna.
        parametros.Add("Inicio", inicio.ToDateTime(TimeOnly.MinValue), DbType.DateTime);
        parametros.Add("FimExclusivo", fim.AddDays(1).ToDateTime(TimeOnly.MinValue), DbType.DateTime);

        await using var conexao = new SqlConnection(tenant.ConnectionString);
        var linhas = await conexao.QueryAsync<VendaDto>(new CommandDefinition(
            SqlVendas,
            parametros,
            commandTimeout: opcoes.Value.TimeoutConsultaSegundos,
            cancellationToken: ct));

        return linhas.AsList();
    }
}
