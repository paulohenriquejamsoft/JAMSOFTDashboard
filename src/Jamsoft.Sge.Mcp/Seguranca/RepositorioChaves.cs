using System.Security.Cryptography;
using Dapper;
using Jamsoft.Sge.Mcp.Configuracao;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Jamsoft.Sge.Mcp.Seguranca;

public interface IRepositorioChaves
{
    /// <summary>Devolve o cliente dono da chave, ou null se a chave for inválida, inativa ou vencida.</summary>
    Task<Tenant?> ValidarAsync(string chave, CancellationToken ct);
}

/// <summary>Lê as api-keys da tabela dbo.McpApiKey do banco central (ConnectionStrings:Master).</summary>
public sealed class RepositorioChaves(
    IConfiguration configuracao,
    IOptions<McpOptions> opcoes,
    ProtetorConexao protetor,
    IMemoryCache cache,
    ILogger<RepositorioChaves> log) : IRepositorioChaves
{
    private readonly string _conexaoMaster = configuracao.GetConnectionString("Master")
        ?? throw new InvalidOperationException("ConnectionStrings:Master não configurada.");

    // Cache curto para chaves inválidas: evita que tentativas repetidas martelem o banco central.
    private static readonly TimeSpan CacheInvalida = TimeSpan.FromSeconds(30);
    private static readonly object Invalida = new();

    private const string Sql = """
        SELECT Id, Cliente, ConnectionStringCifrada, HashChave, Ativo, ExpiraEm
        FROM dbo.McpApiKey
        WHERE Prefixo = @Prefixo
        """;

    public async Task<Tenant?> ValidarAsync(string chave, CancellationToken ct)
    {
        if (!ApiKey.TentarLerPrefixo(chave, out var prefixo)) return null;

        var hash = ApiKey.Hash(chave);
        var chaveCache = "apikey:" + Convert.ToHexString(hash);

        if (cache.TryGetValue(chaveCache, out var emCache))
            return emCache as Tenant;

        var tenant = await BuscarAsync(prefixo, hash, ct);

        cache.Set(chaveCache, (object?)tenant ?? Invalida,
            tenant is null ? CacheInvalida : TimeSpan.FromMinutes(Math.Max(1, opcoes.Value.CacheMinutos)));
        return tenant;
    }

    private async Task<Tenant?> BuscarAsync(string prefixo, byte[] hash, CancellationToken ct)
    {
        await using var conexao = new SqlConnection(_conexaoMaster);
        var registro = await conexao.QueryFirstOrDefaultAsync<RegistroChave>(
            new CommandDefinition(Sql, new { Prefixo = prefixo }, cancellationToken: ct));

        if (registro is null) return null;

        // Comparação em tempo constante: não revela quantos bytes do hash bateram.
        if (registro.HashChave is null || !CryptographicOperations.FixedTimeEquals(registro.HashChave, hash))
            return null;

        if (!registro.Ativo)
        {
            log.LogWarning("Uso de api-key inativa (Id {Id}, cliente {Cliente}).", registro.Id, registro.Cliente);
            return null;
        }

        if (registro.ExpiraEm is { } expira && expira <= DateTime.UtcNow)
        {
            log.LogWarning("Uso de api-key vencida (Id {Id}, cliente {Cliente}).", registro.Id, registro.Cliente);
            return null;
        }

        string conexaoCliente;
        try
        {
            conexaoCliente = protetor.Decifrar(registro.ConnectionStringCifrada);
        }
        catch (CryptographicException ex)
        {
            log.LogError(ex, "Não foi possível decifrar a connection string da api-key Id {Id}. " +
                             "A Mcp:ChaveCriptografia mudou?", registro.Id);
            return null;
        }

        return new Tenant(registro.Id, registro.Cliente, conexaoCliente);
    }

    private sealed class RegistroChave
    {
        public int Id { get; set; }
        public string Cliente { get; set; } = "";
        public string ConnectionStringCifrada { get; set; } = "";
        public byte[]? HashChave { get; set; }
        public bool Ativo { get; set; }
        public DateTime? ExpiraEm { get; set; }
    }
}
