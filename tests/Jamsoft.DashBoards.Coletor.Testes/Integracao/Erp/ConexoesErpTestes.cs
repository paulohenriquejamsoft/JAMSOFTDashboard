using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Coletor.Testes.Integracao.Erp;

/// <summary><see cref="ConexoesErp"/> contra o SQL Server de testes: CFG-03, CFG-04.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ConexoesErpTestes(BancoDeTestes banco)
{
    private const string Senha = "S3nh@-Secreta-do-ERP-7f2c";

    private readonly ConexoesErp _conexoes = new(timeoutConexao: TimeSpan.FromSeconds(5));

    /// <summary>Instância que aponta para o servidor de testes, com a autenticação da connection string da coleção.</summary>
    private InstanciaConexao InstanciaDoServidorDeTestes()
    {
        var conexao = new SqlConnectionStringBuilder(banco.ConnectionString);
        var partes = conexao.DataSource.Split(',');
        int? porta = partes.Length > 1 ? int.Parse(partes[1]) : null;
        return conexao.IntegratedSecurity
            ? new InstanciaConexao(Guid.NewGuid(), partes[0], porta, TipoAutenticacao.Windows, null, null)
            : new InstanciaConexao(Guid.NewGuid(), partes[0], porta, TipoAutenticacao.Sql, conexao.UserID, conexao.Password);
    }

    private InstanciaConexao ComSenhaErrada() =>
        InstanciaDoServidorDeTestes() with { Autenticacao = TipoAutenticacao.Sql, Usuario = "jd_coletor_teste_senha_errada", Senha = Senha };

    private static InstanciaConexao ServidorInexistente() =>
        new(Guid.NewGuid(), "jd-servidor-inexistente.invalid", null, TipoAutenticacao.Sql, "usuario", Senha);

    [Fact]
    public async Task Conexao_valida_lista_os_bancos_incluindo_o_ERP_falso()
    {
        var erp = await ErpFalso.CriarAsync(banco);

        var resultado = await _conexoes.TestarAsync(InstanciaDoServidorDeTestes());

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Null(resultado.Mensagem);
        Assert.Contains(erp.NomeBanco, resultado.Bancos);
        Assert.DoesNotContain("master", resultado.Bancos);
    }

    [Fact]
    public async Task Senha_errada_falha_por_autenticacao()
    {
        var resultado = await _conexoes.TestarAsync(ComSenhaErrada());

        Assert.Equal(FalhaConexao.Autenticacao, resultado.Falha);
        Assert.Empty(resultado.Bancos);
        Assert.StartsWith("Falha de autenticação", resultado.Mensagem);
    }

    [Fact]
    public async Task Servidor_inexistente_falha_por_rede_ou_timeout()
    {
        var resultado = await _conexoes.TestarAsync(ServidorInexistente());

        Assert.Contains(resultado.Falha, new FalhaConexao?[] { FalhaConexao.Rede, FalhaConexao.Timeout });
        Assert.Empty(resultado.Bancos);
    }

    [Fact]
    public async Task Mensagem_de_falha_nunca_contem_a_senha()
    {
        var falhas = new[] { await _conexoes.TestarAsync(ComSenhaErrada()), await _conexoes.TestarAsync(ServidorInexistente()) };

        Assert.All(falhas, f =>
        {
            Assert.False(f.Sucesso);
            Assert.False(string.IsNullOrWhiteSpace(f.Mensagem));
            Assert.DoesNotContain(Senha, f.Mensagem);
            Assert.DoesNotContain("Password", f.Mensagem, StringComparison.OrdinalIgnoreCase);
        });
    }
}
