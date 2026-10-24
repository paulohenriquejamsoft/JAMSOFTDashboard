using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>
/// Banco SQL Server descartável, criado uma vez por coleção de testes.
/// Com a variável TESTES_SQLSERVER (connection string de um servidor existente) cria um banco
/// com nome único nesse servidor e o remove no fim; sem ela, sobe um container via Testcontainers.
/// O nível de compatibilidade fica em 150 (SQL Server 2019), o alvo da plataforma.
/// </summary>
public sealed class BancoDeTestes : IAsyncLifetime
{
    public const string VariavelServidor = "TESTES_SQLSERVER";
    private const int CompatibilidadeSqlServer2019 = 150;
    private const string ImagemSqlServer2019 = "mcr.microsoft.com/mssql/server:2019-latest";

    private MsSqlContainer? _container;
    private string _conexaoServidor = "";

    private readonly SemaphoreSlim _migracao = new(1, 1);
    private bool _migrado;

    public string NomeBanco { get; } = $"JD_Testes_{Guid.NewGuid():N}";
    public string ConnectionString { get; private set; } = "";

    /// <summary>
    /// Contexto sem tenant, para preparar dados e checar esquema: consultas a entidades do tenant
    /// não retornam nada (filtro global), mas a gravação de qualquer tenant é liberada
    /// (<see cref="PlataformaDbContext.OperacaoGlobal"/>) para semear vários tenants de uma vez.
    /// </summary>
    public PlataformaDbContext CriarContexto()
    {
        var contexto = CriarContexto(TenantSemAcesso.Instancia);
        // Fica liberado durante toda a vida do contexto de preparação.
        _ = contexto.OperacaoGlobal();
        return contexto;
    }

    /// <summary>Contexto isolado no tenant informado, como numa requisição.</summary>
    public PlataformaDbContext CriarContexto(ITenantAtual tenantAtual)
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(options, ConnectionString);
        return new PlataformaDbContext(options.Options, tenantAtual);
    }

    /// <summary>Aplica as migrações uma única vez por coleção.</summary>
    public async Task MigrarAsync()
    {
        await _migracao.WaitAsync();
        try
        {
            if (_migrado) return;
            await using var contexto = CriarContexto();
            await contexto.Database.MigrateAsync();
            _migrado = true;
        }
        finally
        {
            _migracao.Release();
        }
    }

    /// <summary>Executa um comando SQL no banco de testes (para checagens de esquema).</summary>
    public async Task<T?> ConsultarEscalarAsync<T>(string sql)
    {
        await using var conexao = new SqlConnection(ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new SqlCommand(sql, conexao);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }

    public async Task InitializeAsync()
    {
        var servidor = Environment.GetEnvironmentVariable(VariavelServidor);
        if (string.IsNullOrWhiteSpace(servidor))
        {
            _container = new MsSqlBuilder(ImagemSqlServer2019).Build();
            await _container.StartAsync();
            servidor = _container.GetConnectionString();
        }

        // Sem pool: conexões administrativas são raras e uma conexão reaproveitada de outra thread pode falhar
        // no provedor de memória compartilhada.
        _conexaoServidor = new SqlConnectionStringBuilder(servidor) { InitialCatalog = "master", Pooling = false }.ConnectionString;
        await ExecutarNoServidorAsync(
            $"CREATE DATABASE [{NomeBanco}]; " +
            $"ALTER DATABASE [{NomeBanco}] SET COMPATIBILITY_LEVEL = {CompatibilidadeSqlServer2019};");

        ConnectionString = new SqlConnectionStringBuilder(servidor) { InitialCatalog = NomeBanco }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        SqlConnection.ClearAllPools();
        foreach (var nome in _bancosExtras.Append(NomeBanco))
            await ExecutarNoServidorAsync(
                $"IF DB_ID('{nome}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{nome}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nome}]; END");
    }

    private readonly List<string> _bancosExtras = [];

    /// <summary>Cria outro banco vazio no mesmo servidor (removido no fim) e devolve a connection string dele.</summary>
    public async Task<string> CriarBancoVazioAsync()
    {
        var nome = $"JD_Testes_{Guid.NewGuid():N}";
        lock (_bancosExtras) _bancosExtras.Add(nome);
        await ExecutarNoServidorAsync(
            $"CREATE DATABASE [{nome}]; ALTER DATABASE [{nome}] SET COMPATIBILITY_LEVEL = {CompatibilidadeSqlServer2019};");
        return new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = nome }.ConnectionString;
    }

    private async Task ExecutarNoServidorAsync(string sql)
    {
        await using var conexao = new SqlConnection(_conexaoServidor);
        await conexao.OpenAsync();
        await using var comando = new SqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoIntegracao : ICollectionFixture<BancoDeTestes>
{
    public const string Nome = "Integracao";
}
