using Dapper;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Erp;

/// <summary>Tipo de falha do teste de conexão mostrado ao técnico (CFG-04).</summary>
public enum FalhaConexao
{
    Rede,
    Autenticacao,
    Timeout,
}

/// <summary>
/// Resultado do teste de conexão (CFG-03/04): os bancos acessíveis ou o tipo de falha com uma mensagem que nunca
/// traz a senha nem a connection string.
/// </summary>
public sealed record ResultadoTesteConexao(IReadOnlyList<string> Bancos, FalhaConexao? Falha, string? Mensagem)
{
    public bool Sucesso => Falha is null;
}

/// <summary>
/// Monta a connection string de uma <see cref="InstanciaConexao"/> (só na memória, nunca gravada), testa a conexão e
/// lista os bancos (CFG-03), classificando falhas em rede, autenticação ou timeout (CFG-04).
/// </summary>
public sealed class ConexoesErp(TimeSpan? timeoutConexao = null)
{
    public static readonly TimeSpan TimeoutConexaoPadrao = TimeSpan.FromSeconds(15);
    public const string NomeAplicacao = "Jamsoft.DashBoards.Coletor";

    private readonly int _timeoutSegundos = Math.Max(1, (int)Math.Ceiling((timeoutConexao ?? TimeoutConexaoPadrao).TotalSeconds));

    public string ConnectionString(InstanciaConexao instancia, string? banco = null)
    {
        var construtor = new SqlConnectionStringBuilder
        {
            DataSource = instancia.Porta is { } porta ? $"{instancia.Servidor},{porta}" : instancia.Servidor,
            InitialCatalog = banco ?? "master",
            ApplicationName = NomeAplicacao,
            ConnectTimeout = _timeoutSegundos,
            // Servidores de ERP costumam ter certificado próprio: canal cifrado, sem validar a cadeia.
            TrustServerCertificate = true,
        };
        if (instancia.Autenticacao == TipoAutenticacao.Windows)
        {
            construtor.IntegratedSecurity = true;
        }
        else
        {
            construtor.UserID = instancia.Usuario ?? "";
            construtor.Password = instancia.Senha ?? "";
        }

        return construtor.ConnectionString;
    }

    public async Task<ResultadoTesteConexao> TestarAsync(InstanciaConexao instancia, CancellationToken ct = default)
    {
        // Sem pool: o teste precisa abrir uma conexão de verdade a cada clique.
        var texto = new SqlConnectionStringBuilder(ConnectionString(instancia)) { Pooling = false }.ConnectionString;
        try
        {
            await using var conexao = new SqlConnection(texto);
            await conexao.OpenAsync(ct);
            var bancos = await conexao.QueryAsync<string>(new CommandDefinition("""
                SELECT name FROM sys.databases
                 WHERE database_id > 4 AND state_desc = 'ONLINE' AND HAS_DBACCESS(name) = 1
                 ORDER BY name;
                """, commandTimeout: _timeoutSegundos, cancellationToken: ct));
            return new ResultadoTesteConexao([.. bancos], null, null);
        }
        catch (SqlException erro)
        {
            var falha = Classificar(erro);
            return new ResultadoTesteConexao([], falha, Mensagem(falha, erro.Message, instancia.Senha));
        }
        catch (InvalidOperationException erro) when (!ct.IsCancellationRequested)
        {
            // Esgotou o tempo antes de obter a conexão.
            return new ResultadoTesteConexao([], FalhaConexao.Timeout, Mensagem(FalhaConexao.Timeout, erro.Message, instancia.Senha));
        }
    }

    private static FalhaConexao Classificar(SqlException erro) => erro.Number switch
    {
        18456 or 18452 or 18486 or 18487 or 18488 => FalhaConexao.Autenticacao,
        -2 or 258 => FalhaConexao.Timeout,
        _ => FalhaConexao.Rede,
    };

    private static string Mensagem(FalhaConexao falha, string tecnica, string? senha)
    {
        var resumo = falha switch
        {
            FalhaConexao.Autenticacao => "Falha de autenticação: usuário ou senha recusados pelo servidor.",
            FalhaConexao.Timeout => "Tempo esgotado ao conectar ao servidor.",
            _ => "Falha de rede: servidor não encontrado ou inacessível.",
        };
        var detalhe = string.IsNullOrEmpty(senha) ? tecnica : tecnica.Replace(senha, "***", StringComparison.Ordinal);
        return $"{resumo} Detalhe: {detalhe}";
    }
}
