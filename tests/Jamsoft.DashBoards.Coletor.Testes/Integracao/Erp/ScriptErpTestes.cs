using System.Text.RegularExpressions;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Coletor.Testes.Integracao.Erp;

/// <summary><c>sql/coletor/001_erp_usuario_leitura_e_indices.sql</c> aplicado no ERP falso.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed partial class ScriptErpTestes(BancoDeTestes banco)
{
    private const string Usuario = "jd_coletor_leitura";

    [Fact]
    public async Task Script_aplica_duas_vezes_e_o_usuario_le_so_pela_view_com_os_indices_criados()
    {
        var erp = await ErpFalso.CriarAsync(banco);
        var dia = new DateTime(2026, 3, 10);
        var nota = await erp.InserirAsync(new NotaFalsa(dia, Itens: 2));

        await AplicarScriptAsync(erp);
        await AplicarScriptAsync(erp);

        // Usuário sem login de servidor: a conexão do dono assume a identidade dele com EXECUTE AS USER.
        var semPool = new SqlConnectionStringBuilder(erp.ConnectionString) { Pooling = false }.ConnectionString;
        async Task<SqlConnection> ComoUsuarioAsync(CancellationToken ct)
        {
            var conexao = new SqlConnection(semPool);
            await conexao.OpenAsync(ct);
            await using var comando = new SqlCommand($"EXECUTE AS USER = '{Usuario}';", conexao);
            await comando.ExecuteNonQueryAsync(ct);
            return conexao;
        }

        var venda = Assert.Single(await new ExtratorVendas(ComoUsuarioAsync).LerLoteAsync(dia, dia.AddDays(1), cursor: null));
        Assert.Equal((nota, 2), (venda.VendaId, venda.QtdItens));

        await using (var conexao = await ComoUsuarioAsync(CancellationToken.None))
        await using (var direto = new SqlCommand("SELECT COUNT(*) FROM dbo.NotaSaida;", conexao))
        {
            var negado = await Assert.ThrowsAsync<SqlException>(() => direto.ExecuteScalarAsync());
            Assert.Equal(229, negado.Number); // permissão SELECT negada
        }

        Assert.Equal(2, await erp.ConsultarAsync<int>("""
            SELECT COUNT(*) FROM sys.indexes
             WHERE (object_id = OBJECT_ID(N'dbo.NotaSaida') AND name = N'IX_NotaSaida_DataFatura_NumNotaSaida')
                OR (object_id = OBJECT_ID(N'dbo.NotaSaidaProd') AND name = N'IX_NotaSaidaProd_NumNotaSaida');
            """));
    }

    private static async Task AplicarScriptAsync(ErpFalso erp)
    {
        var script = await File.ReadAllTextAsync(CaminhoScript());
        foreach (var lote in SeparadorGo().Split(script).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            await erp.ExecutarAsync(lote);
        }
    }

    private static string CaminhoScript()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, "Jamsoft.DashBoards.slnx")))
        {
            pasta = pasta.Parent;
        }

        return Path.Combine(pasta?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada."),
            "sql", "coletor", "001_erp_usuario_leitura_e_indices.sql");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();
}
