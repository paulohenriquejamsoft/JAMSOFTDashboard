using System.Data;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>
/// Grava as vendas de um lote com um único <c>MERGE ... WITH (HOLDLOCK)</c> alimentado por <c>OPENJSON</c> (AD-014,
/// ING-04): insere a venda nova e atualiza todos os campos da que já existe na chave (TENANT_ID, COD_EMPRESA,
/// VENDA_ID). O SQL é fixo e parametrizado; o tenant vem do parâmetro (o tenant do lote, já autorizado), nunca do
/// JSON. Roda na conexão e na transação atuais do <see cref="PlataformaDbContext"/>.
/// </summary>
public static class GravadorVendas
{
    // OUTPUT INTO: OUTPUT direto ao cliente não é permitido em tabela com trigger.
    private const string Sql = """
        DECLARE @acoes TABLE (ACAO nvarchar(10) NOT NULL);
        MERGE dbo.VENDA WITH (HOLDLOCK) AS v
        USING (SELECT @TenantId AS TENANT_ID, j.* FROM OPENJSON(@Vendas) WITH (
                 COD_EMPRESA int, VENDA_ID int, EMPRESA_ID int, TIPO_DFE varchar(6), DATA_HORA datetimeoffset(0),
                 VALOR decimal(19,4), COD_CLIENTE int, NOME_CLIENTE nvarchar(120), VENDEDOR_CODIGO int,
                 VENDEDOR_NOME nvarchar(80), SITUACAO nvarchar(12), QTD_ITENS int) AS j) AS s
           ON v.TENANT_ID = s.TENANT_ID AND v.COD_EMPRESA = s.COD_EMPRESA AND v.VENDA_ID = s.VENDA_ID
        WHEN MATCHED THEN UPDATE SET
            EMPRESA_ID = s.EMPRESA_ID, TIPO_DFE = s.TIPO_DFE, DATA_HORA = s.DATA_HORA, VALOR = s.VALOR,
            COD_CLIENTE = s.COD_CLIENTE, NOME_CLIENTE = s.NOME_CLIENTE, VENDEDOR_CODIGO = s.VENDEDOR_CODIGO,
            VENDEDOR_NOME = s.VENDEDOR_NOME, SITUACAO = s.SITUACAO, QTD_ITENS = s.QTD_ITENS,
            ALTERADO_EM = SYSUTCDATETIME(), ORIGEM_ID = @OrigemId
        WHEN NOT MATCHED THEN INSERT
            (TENANT_ID, EMPRESA_ID, COD_EMPRESA, VENDA_ID, TIPO_DFE, DATA_HORA, VALOR, COD_CLIENTE, NOME_CLIENTE,
             VENDEDOR_CODIGO, VENDEDOR_NOME, SITUACAO, QTD_ITENS, ORIGEM_ID)
            VALUES (s.TENANT_ID, s.EMPRESA_ID, s.COD_EMPRESA, s.VENDA_ID, s.TIPO_DFE, s.DATA_HORA, s.VALOR, s.COD_CLIENTE,
             s.NOME_CLIENTE, s.VENDEDOR_CODIGO, s.VENDEDOR_NOME, s.SITUACAO, s.QTD_ITENS, @OrigemId)
        OUTPUT $action INTO @acoes;
        SELECT ISNULL(SUM(CASE WHEN ACAO = 'INSERT' THEN 1 ELSE 0 END), 0) AS Inseridos,
               ISNULL(SUM(CASE WHEN ACAO = 'UPDATE' THEN 1 ELSE 0 END), 0) AS Atualizados
        FROM @acoes;
        """;

    /// <summary>Linha de contagem devolvida pelo MERGE.</summary>
    public sealed class Contagem
    {
        public int Inseridos { get; set; }
        public int Atualizados { get; set; }
    }

    /// <summary>
    /// Grava <paramref name="vendas"/> (já validadas, sem chave repetida) do tenant <paramref name="tenantId"/>.
    /// <paramref name="empresas"/> leva o ID interno de cada <c>codEmpresa</c> do lote.
    /// </summary>
    public static async Task<(int Inseridos, int Atualizados)> GravarAsync(PlataformaDbContext banco, int tenantId,
        int origemId, IReadOnlyList<VendaContrato> vendas, IReadOnlyDictionary<int, int> empresas, CancellationToken ct = default)
    {
        if (vendas.Count == 0) return (0, 0);

        var json = JsonSerializer.Serialize(vendas.Select(v => new
        {
            COD_EMPRESA = v.CodEmpresa,
            VENDA_ID = v.VendaId,
            EMPRESA_ID = empresas[v.CodEmpresa],
            TIPO_DFE = v.TipoDfe,
            DATA_HORA = v.DataHora,
            VALOR = v.Valor,
            COD_CLIENTE = v.CodCliente,
            NOME_CLIENTE = v.NomeCliente,
            VENDEDOR_CODIGO = v.VendedorCodigo,
            VENDEDOR_NOME = v.VendedorNome,
            SITUACAO = v.Situacao,
            QTD_ITENS = v.QtdItens,
        }));

        var contagem = (await banco.Database.SqlQueryRaw<Contagem>(Sql,
            new SqlParameter("@TenantId", SqlDbType.Int) { Value = tenantId },
            new SqlParameter("@OrigemId", SqlDbType.Int) { Value = origemId },
            new SqlParameter("@Vendas", SqlDbType.NVarChar, -1) { Value = json }).ToListAsync(ct)).Single();
        return (contagem.Inseridos, contagem.Atualizados);
    }
}
