using System.Security.Cryptography;
using Dapper;
using Jamsoft.DashBoards.Contratos.Mcp;
using Jamsoft.Sge.Mcp.Configuracao;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Jamsoft.Sge.Mcp.Seguranca;

public interface IRepositorioChaves
{
    /// <summary>Tenant dono da chave, ou null se a chave for inválida, inativa, vencida ou o tenant estiver inativo.</summary>
    Task<Tenant?> ValidarAsync(string chave, CancellationToken ct);
}

/// <summary>
/// Valida a api-key na tabela CHAVE_MCP do banco da plataforma (ConnectionStrings:Plataforma), emitida pela API
/// com <c>dotnet run -- gerar-chave-mcp</c>. Tenant inativo (inclusive em exclusão) recusa a chave.
/// </summary>
public sealed class RepositorioChaves(
    IConfiguration configuracao,
    IOptions<McpOptions> opcoes,
    IMemoryCache cache,
    ILogger<RepositorioChaves> log) : IRepositorioChaves
{
    private readonly string _conexao = configuracao.GetConnectionString("Plataforma")
        ?? throw new InvalidOperationException("ConnectionStrings:Plataforma não configurada.");

    // Cache curto para chaves inválidas: evita que tentativas repetidas martelem o banco.
    private static readonly TimeSpan CacheInvalida = TimeSpan.FromSeconds(30);
    private static readonly object Invalida = new();

    private const string Sql = """
        SELECT c.ID          AS ChaveId,
               c.HASH        AS Hash,
               c.ATIVO       AS Ativo,
               c.EXPIRA_EM   AS ExpiraEm,
               t.ID          AS TenantId,
               t.ID_PUBLICO  AS TenantIdPublico,
               t.NOME        AS TenantNome,
               t.ATIVO       AS TenantAtivo
        FROM dbo.CHAVE_MCP AS c
        JOIN dbo.TENANT    AS t ON t.ID = c.TENANT_ID
        WHERE c.PREFIXO = @Prefixo
        """;

    public async Task<Tenant?> ValidarAsync(string chave, CancellationToken ct)
    {
        if (!FormatoChaveMcp.TentarLerPrefixo(chave, out var prefixo)) return null;

        var hash = FormatoChaveMcp.Hash(chave);
        var chaveCache = "chave-mcp:" + Convert.ToHexString(hash);

        if (cache.TryGetValue(chaveCache, out var emCache))
            return emCache as Tenant;

        var tenant = await BuscarAsync(prefixo, hash, ct);

        cache.Set(chaveCache, (object?)tenant ?? Invalida,
            tenant is null ? CacheInvalida : TimeSpan.FromSeconds(opcoes.Value.CacheSegundos));
        return tenant;
    }

    private async Task<Tenant?> BuscarAsync(string prefixo, byte[] hash, CancellationToken ct)
    {
        await using var conexao = new SqlConnection(_conexao);
        // PREFIXO é char(8): parâmetro do mesmo tipo, sem conversão implícita que impeça o índice.
        var parametro = new DbString { Value = prefixo, IsAnsi = true, IsFixedLength = true, Length = FormatoChaveMcp.TamanhoPrefixo };
        var registro = await conexao.QueryFirstOrDefaultAsync<RegistroChave>(
            new CommandDefinition(Sql, new { Prefixo = parametro }, cancellationToken: ct));

        if (registro is null) return null;

        // Comparação em tempo constante: não revela quantos bytes do hash bateram.
        if (!CryptographicOperations.FixedTimeEquals(registro.Hash, hash)) return null;

        if (!registro.Ativo)
        {
            log.LogWarning("Uso de chave MCP revogada ({Prefixo}, tenant {Tenant}).", prefixo, registro.TenantIdPublico);
            return null;
        }

        if (registro.ExpiraEm is { } expira && expira <= DateTime.UtcNow)
        {
            log.LogWarning("Uso de chave MCP vencida ({Prefixo}, tenant {Tenant}).", prefixo, registro.TenantIdPublico);
            return null;
        }

        if (!registro.TenantAtivo)
        {
            log.LogWarning("Uso de chave MCP de tenant inativo ({Prefixo}, tenant {Tenant}).", prefixo, registro.TenantIdPublico);
            return null;
        }

        return new Tenant(registro.TenantId, registro.TenantIdPublico, registro.TenantNome, prefixo);
    }

    private sealed class RegistroChave
    {
        public int ChaveId { get; set; }
        public byte[] Hash { get; set; } = [];
        public bool Ativo { get; set; }
        public DateTime? ExpiraEm { get; set; }
        public int TenantId { get; set; }
        public Guid TenantIdPublico { get; set; }
        public string TenantNome { get; set; } = "";
        public bool TenantAtivo { get; set; }
    }
}
