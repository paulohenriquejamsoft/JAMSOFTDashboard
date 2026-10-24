using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
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

    public PlataformaDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(options, ConnectionString);
        return new PlataformaDbContext(options.Options);
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

        _conexaoServidor = new SqlConnectionStringBuilder(servidor) { InitialCatalog = "master" }.ConnectionString;
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
        await ExecutarNoServidorAsync(
            $"IF DB_ID('{NomeBanco}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{NomeBanco}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{NomeBanco}]; END");
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
