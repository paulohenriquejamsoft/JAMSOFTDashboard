using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Carga de dados de um tenant direto no banco e "impressão digital" das linhas dele.</summary>
public static class DadosTenant
{
    /// <summary>
    /// Insere, para o tenant, <paramref name="empresas"/> linhas em EMPRESA e <paramref name="usuarios"/> usuários
    /// Consulta, cada um com um refresh token (TENANT_ID copiado).
    /// </summary>
    public static Task SemearAsync(BancoDeTestes banco, int tenantId, int empresas, int usuarios) =>
        banco.ExecutarAsync($"""
            WITH n AS (SELECT TOP ({empresas}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                       FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO EMPRESA (TENANT_ID, COD_EMPRESA, NOME) SELECT {tenantId}, i, CONCAT('Empresa ', i) FROM n;

            WITH n AS (SELECT TOP ({usuarios}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                       FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO USUARIO (TENANT_ID, NOME, EMAIL, SENHA_HASH, PERFIL)
            SELECT {tenantId}, CONCAT('Carga ', i), CONCAT(LOWER(CONVERT(char(36), NEWID())), '@carga.com'), 'hash', 'Consulta'
            FROM n;

            INSERT INTO REFRESH_TOKEN (USUARIO_ID, TENANT_ID, HASH, FAMILIA, EXPIRA_EM)
            SELECT u.ID, u.TENANT_ID, HASHBYTES('SHA2_256', CONVERT(nvarchar(36), NEWID())), NEWID(),
                   DATEADD(DAY, 7, SYSUTCDATETIME())
            FROM USUARIO u WHERE u.TENANT_ID = {tenantId};
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
