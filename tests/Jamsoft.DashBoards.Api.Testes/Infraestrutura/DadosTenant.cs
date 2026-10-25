using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Carga de dados de um tenant direto no banco e "impressão digital" das linhas dele.</summary>
public static class DadosTenant
{
    /// <summary>
    /// Insere, para o tenant, <paramref name="empresas"/> linhas em EMPRESA e <paramref name="usuarios"/> usuários
    /// Consulta, cada um com um refresh token (TENANT_ID copiado).
    /// </summary>
    /// <remarks>
    /// A sequência 1..N sai de uma tabela de dígitos em <c>VALUES</c> (até 100.000 linhas), sem ler catálogo.
    /// Com <c>sys.all_objects CROSS JOIN sys.all_objects</c>, com o SQL Server sob pressão de memória, inserir 10.000
    /// linhas levava de 37 a 83 s (PAGEIOLATCH no banco de recursos) contra 0,1 s assim; somadas, as cargas da
    /// exclusão chegavam ao tempo limite de 2 min de <see cref="BancoDeTestes.ExecutarAsync"/>.
    /// </remarks>
    public static Task SemearAsync(BancoDeTestes banco, int tenantId, int empresas, int usuarios) =>
        banco.ExecutarAsync($"""
            WITH d AS (SELECT v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) AS t(v)),
                 n AS (SELECT TOP ({empresas}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                       FROM d a CROSS JOIN d b CROSS JOIN d c CROSS JOIN d e CROSS JOIN d f)
            INSERT INTO EMPRESA (TENANT_ID, COD_EMPRESA, NOME) SELECT {tenantId}, i, CONCAT('Empresa ', i) FROM n;

            WITH d AS (SELECT v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) AS t(v)),
                 n AS (SELECT TOP ({usuarios}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                       FROM d a CROSS JOIN d b CROSS JOIN d c CROSS JOIN d e CROSS JOIN d f)
            INSERT INTO USUARIO (TENANT_ID, NOME, EMAIL, SENHA_HASH, PERFIL)
            SELECT {tenantId}, CONCAT('Carga ', i), CONCAT(LOWER(CONVERT(char(36), NEWID())), '@carga.com'), 'hash', 'Consulta'
            FROM n;

            INSERT INTO REFRESH_TOKEN (USUARIO_ID, TENANT_ID, HASH, FAMILIA, EXPIRA_EM)
            SELECT u.ID, u.TENANT_ID, HASHBYTES('SHA2_256', CONVERT(nvarchar(36), NEWID())), NEWID(),
                   DATEADD(DAY, 7, SYSUTCDATETIME())
            FROM USUARIO u WHERE u.TENANT_ID = {tenantId};
            """);

    /// <summary>
    /// Insere, para o tenant, a autorização do coletor (COLETOR_TENANT), uma ORIGEM_SINCRONIZACAO dona de uma EMPRESA,
    /// <paramref name="vendas"/> linhas em VENDA e <paramref name="lotes"/> em LOTE_INGESTAO, todas dessa origem.
    /// </summary>
    public static Task SemearSincronizacaoAsync(BancoDeTestes banco, int tenantId, int coletorId, int vendas, int lotes) =>
        banco.ExecutarAsync($"""
            INSERT INTO COLETOR_TENANT (TENANT_ID, COLETOR_ID) VALUES ({tenantId}, {coletorId});

            INSERT INTO ORIGEM_SINCRONIZACAO (TENANT_ID, COLETOR_ID, CONFIGURACAO_ID, ROTINA)
            VALUES ({tenantId}, {coletorId}, NEWID(), 'Vendas');
            DECLARE @origem int = SCOPE_IDENTITY();

            INSERT INTO EMPRESA (TENANT_ID, COD_EMPRESA, NOME, ORIGEM_ID) VALUES ({tenantId}, 1, 'Loja sincronizada', @origem);
            DECLARE @empresa int = SCOPE_IDENTITY();

            WITH d AS (SELECT v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) AS t(v)),
                 n AS (SELECT TOP ({vendas}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                       FROM d a CROSS JOIN d b CROSS JOIN d c CROSS JOIN d e CROSS JOIN d f)
            INSERT INTO VENDA (TENANT_ID, EMPRESA_ID, COD_EMPRESA, VENDA_ID, TIPO_DFE, DATA_HORA, VALOR, SITUACAO,
                               QTD_ITENS, ORIGEM_ID)
            SELECT {tenantId}, @empresa, 1, i, 'NFC-e', SYSDATETIMEOFFSET(), 10, N'Finalizada', 1, @origem FROM n;

            WITH d AS (SELECT v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) AS t(v)),
                 n AS (SELECT TOP ({lotes}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM d a CROSS JOIN d b)
            INSERT INTO LOTE_INGESTAO (TENANT_ID, BATCH_ID, ORIGEM_ID, INSERIDOS, ATUALIZADOS, REJEITADOS, RESPOSTA)
            SELECT {tenantId}, NEWID(), @origem, 1, 0, 0, N'[]' FROM n;
            """);

    /// <summary>Tabelas de dbo com a coluna TENANT_ID.</summary>
    public static async Task<List<string>> TabelasComTenantIdAsync(BancoDeTestes banco)
    {
        var tabelas = new List<string>();
        await using var conexao = new SqlConnection(banco.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new SqlCommand(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND COLUMN_NAME = 'TENANT_ID' " +
            "ORDER BY TABLE_NAME", conexao);
        await using var leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync()) tabelas.Add(leitor.GetString(0));
        return tabelas;
    }

    /// <summary>
    /// Por tabela com TENANT_ID (e TENANT pelo ID): quantidade de linhas e <c>CHECKSUM_AGG(BINARY_CHECKSUM(*))</c>
    /// das linhas do tenant. Qualquer inserção, alteração (inclusive ROWVERSION) ou exclusão muda o resultado.
    /// </summary>
    public static async Task<Dictionary<string, string>> ImpressaoAsync(BancoDeTestes banco, int tenantId)
    {
        var consultas = (await TabelasComTenantIdAsync(banco)).Select(t => (t, "TENANT_ID")).Append(("TENANT", "ID"));
        var impressao = new Dictionary<string, string>();
        await using var conexao = new SqlConnection(banco.ConnectionString);
        await conexao.OpenAsync();
        foreach (var (tabela, coluna) in consultas)
        {
            await using var comando = new SqlCommand(
                $"SELECT COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM [{tabela}] WHERE [{coluna}] = {tenantId}", conexao);
            await using var leitor = await comando.ExecuteReaderAsync();
            await leitor.ReadAsync();
            impressao[tabela] = $"{leitor.GetInt64(0)} linhas, checksum {(leitor.IsDBNull(1) ? "-" : leitor.GetInt32(1))}";
        }
        return impressao;
    }

    /// <summary>Linhas do tenant por tabela com TENANT_ID (exceto EXCLUSAO_TENANT) e em TENANT.</summary>
    public static async Task<Dictionary<string, long>> LinhasAsync(BancoDeTestes banco, int tenantId)
    {
        var linhas = new Dictionary<string, long>();
        foreach (var tabela in (await TabelasComTenantIdAsync(banco)).Where(t => t != "EXCLUSAO_TENANT"))
            linhas[tabela] = await banco.ConsultarEscalarAsync<long>(
                $"SELECT COUNT_BIG(*) FROM [{tabela}] WHERE TENANT_ID = {tenantId}");
        linhas["TENANT"] = await banco.ConsultarEscalarAsync<long>($"SELECT COUNT_BIG(*) FROM TENANT WHERE ID = {tenantId}");
        return linhas;
    }
}
