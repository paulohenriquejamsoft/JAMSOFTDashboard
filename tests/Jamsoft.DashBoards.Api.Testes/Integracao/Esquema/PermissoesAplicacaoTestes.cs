using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class PermissoesAplicacaoTestes(BancoDeTestes banco) : IAsyncLifetime
{
    /// <summary>Erro do SQL Server para permissão negada no objeto.</summary>
    private const int ErroPermissaoNegada = 229;

    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string Script()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, "Jamsoft.DashBoards.slnx"))) pasta = pasta.Parent;
        return File.ReadAllText(Path.Combine(pasta!.FullName, "sql", "001_permissoes_aplicacao.sql"));
    }

    private static async Task<int> ExecutarAsync(SqlConnection conexao, string sql)
    {
        await using var comando = new SqlCommand(sql, conexao);
        return await comando.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Role_da_aplicacao_insere_na_AUDITORIA_mas_nao_altera_nem_exclui()
    {
        // Usuário de banco sem login de servidor: o teste não cria logins na instância.
        var usuario = $"teste_app_{Guid.NewGuid():N}";
        var operacao = $"PERM_{Guid.NewGuid():N}"[..30].ToUpperInvariant();
        // Sem pool: a conexão com EXECUTE AS não volta para ser reaproveitada por outro teste.
        await using var conexao = new SqlConnection(
            new SqlConnectionStringBuilder(banco.ConnectionString) { Pooling = false }.ConnectionString);
        await conexao.OpenAsync();
        // Idempotente: aplicar duas vezes não falha.
        await ExecutarAsync(conexao, Script());
        await ExecutarAsync(conexao, Script());
        await ExecutarAsync(conexao, $"""
            CREATE USER [{usuario}] WITHOUT LOGIN;
            ALTER ROLE jamsoft_dashboards_app ADD MEMBER [{usuario}];
            INSERT INTO dbo.AUDITORIA (OCORRIDO_EM, OPERACAO, RESULTADO) VALUES (SYSUTCDATETIME(), '{operacao}', 'Sucesso');
            """);

        await ExecutarAsync(conexao, $"EXECUTE AS USER = '{usuario}';");
        var inseridas = await ExecutarAsync(conexao,
            $"INSERT INTO dbo.AUDITORIA (OCORRIDO_EM, OPERACAO, RESULTADO) VALUES (SYSUTCDATETIME(), '{operacao}', 'Falha');");
        var alterar = await Assert.ThrowsAsync<SqlException>(() =>
            ExecutarAsync(conexao, $"UPDATE dbo.AUDITORIA SET RESULTADO = 'Negado' WHERE OPERACAO = '{operacao}';"));
        var excluir = await Assert.ThrowsAsync<SqlException>(() =>
            ExecutarAsync(conexao, $"DELETE FROM dbo.AUDITORIA WHERE OPERACAO = '{operacao}';"));
        // Nas demais tabelas a role altera normalmente.
        var tenantsAlterados = await ExecutarAsync(conexao, "UPDATE dbo.TENANT SET NOME = NOME WHERE 1 = 0;");
        await ExecutarAsync(conexao, "REVERT;");

        Assert.Equal(1, inseridas);
        Assert.Equal(ErroPermissaoNegada, alterar.Number);
        Assert.Equal(ErroPermissaoNegada, excluir.Number);
        Assert.Equal(0, tenantsAlterados);
        Assert.Equal("Falha,Sucesso", await banco.ConsultarEscalarAsync<string>($"""
            SELECT STRING_AGG(RESULTADO, ',') WITHIN GROUP (ORDER BY RESULTADO)
            FROM dbo.AUDITORIA WHERE OPERACAO = '{operacao}'
            """));
    }
}
