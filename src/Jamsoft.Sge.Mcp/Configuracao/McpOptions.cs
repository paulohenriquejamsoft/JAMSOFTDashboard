namespace Jamsoft.Sge.Mcp.Configuracao;

/// <summary>Configurações da seção "Mcp" do appsettings.json.</summary>
public sealed class McpOptions
{
    public const string Secao = "Mcp";

    /// <summary>
    /// Por quantos segundos uma api-key válida fica em cache antes de ir de novo ao banco. É também o prazo para
    /// valerem a revogação da chave e a inativação do tenant (mesmos 60 s da API).
    /// </summary>
    public int CacheSegundos { get; set; } = 60;

    /// <summary>
    /// Aceita a chave também na URL (?api_key=...). Só ligue se o cliente MCP não conseguir
    /// enviar cabeçalhos: a URL costuma aparecer em logs de proxy e do IIS.
    /// </summary>
    public bool PermitirChaveNaQuery { get; set; }

    /// <summary>Maior intervalo de datas aceito por consulta, para proteger o banco da plataforma.</summary>
    public int MaxDiasConsulta { get; set; } = 400;

    /// <summary>Tempo máximo de cada consulta no banco, em segundos.</summary>
    public int TimeoutConsultaSegundos { get; set; } = 60;

    public bool Valida() => CacheSegundos > 0 && MaxDiasConsulta > 0 && TimeoutConsultaSegundos > 0;
}
