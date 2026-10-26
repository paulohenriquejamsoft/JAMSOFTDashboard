namespace Jamsoft.Sge.Mcp.Configuracao;

/// <summary>Configurações da seção "Mcp" do appsettings.json.</summary>
public sealed class McpOptions
{
    public const string Secao = "Mcp";

    /// <summary>
    /// Chave AES-256 (32 bytes em Base64) usada para cifrar as connection strings
    /// guardadas no banco central. Gere com: dotnet run -- gerar-chave-mestra
    /// Nunca deixe no appsettings.json versionado: use user-secrets ou variável de ambiente
    /// (Mcp__ChaveCriptografia).
    /// </summary>
    public string ChaveCriptografia { get; set; } = "";

    /// <summary>Por quantos minutos uma api-key válida fica em cache antes de ir de novo ao banco central.</summary>
    public int CacheMinutos { get; set; } = 5;

    /// <summary>
    /// Aceita a chave também na URL (?api_key=...). Só ligue se o cliente MCP não conseguir
    /// enviar cabeçalhos: a URL costuma aparecer em logs de proxy e do IIS.
    /// </summary>
    public bool PermitirChaveNaQuery { get; set; }

    /// <summary>Maior intervalo de datas aceito por consulta, para proteger o banco do cliente.</summary>
    public int MaxDiasConsulta { get; set; } = 400;

    /// <summary>Tempo máximo de cada consulta no banco do cliente, em segundos.</summary>
    public int TimeoutConsultaSegundos { get; set; } = 60;
}
