using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Logs;

public enum NivelLog
{
    Informacao,
    Aviso,
    Erro,
}

/// <summary>Categorias de erro do LOG-03.</summary>
public enum TipoErro
{
    [JsonStringEnumMemberName("Conexão")]
    Conexao,
    Consulta,
    [JsonStringEnumMemberName("Comunicação")]
    Comunicacao,
    Processamento,
}

public enum ResultadoExecucao
{
    Sucesso,
    Falha,
    Cancelada,
}

/// <summary>Quem está executando: vai em toda linha do log (LOG-01). Nenhum campo aceita segredo.</summary>
public sealed record ContextoExecucao(Guid ExecucaoId, Guid Tenant, string NomeTenant, string Instancia, string Banco, string Rotina);

/// <summary>Uma linha do arquivo de log. Os campos de fim só vêm no evento <c>execucao_fim</c> (LOG-02).</summary>
public sealed record EventoLog(
    DateTimeOffset DataHora,
    NivelLog Nivel,
    string Evento,
    Guid ExecucaoId,
    Guid Tenant,
    string NomeTenant,
    string Instancia,
    string Banco,
    string Rotina,
    string? Mensagem = null,
    TipoErro? TipoErro = null,
    DateTimeOffset? Inicio = null,
    DateTimeOffset? Fim = null,
    TimeSpan? Duracao = null,
    int? Lidos = null,
    int? Enviados = null,
    int? Rejeitados = null,
    ResultadoExecucao? Resultado = null);

/// <summary>
/// Log diário <c>coletor-AAAAMMDD.jsonl</c>, uma linha JSON por evento (LOG-01). Mensagens passam por um filtro de
/// <c>Password=</c>/<c>Pwd=</c> (LOG-04). <see cref="LimparAntigos"/> aplica a retenção de 30 dias (LOG-06).
/// Uma instância por pasta (singleton): o lock serializa as execuções paralelas.
/// </summary>
public sealed partial class LogExecucoes
{
    public const int DiasRetencao = 30;
    public const string EventoFim = "execucao_fim";
    public const string EventoErro = "erro";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly string _pasta;
    private readonly TimeProvider _relogio;
    private readonly Lock _lock = new();

    public LogExecucoes(string pastaLogs, TimeProvider relogio)
    {
        _pasta = Directory.CreateDirectory(pastaLogs).FullName;
        _relogio = relogio;
    }

    public void Registrar(ContextoExecucao contexto, NivelLog nivel, string evento, string mensagem) =>
        Escrever(Evento(contexto, nivel, evento) with { Mensagem = Sanitizar(mensagem) });

    /// <summary>Erro classificado com a mensagem técnica (LOG-03).</summary>
    public void RegistrarErro(ContextoExecucao contexto, TipoErro tipo, string mensagemTecnica) =>
        Escrever(Evento(contexto, NivelLog.Erro, EventoErro) with { TipoErro = tipo, Mensagem = Sanitizar(mensagemTecnica) });

    /// <summary>Fim da execução: o término é agora (LOG-02).</summary>
    public void RegistrarFim(ContextoExecucao contexto, DateTimeOffset inicio, int lidos, int enviados, int rejeitados, ResultadoExecucao resultado)
    {
        var evento = Evento(contexto, NivelLog.Informacao, EventoFim);
        Escrever(evento with
        {
            Inicio = inicio,
            Fim = evento.DataHora,
            Duracao = evento.DataHora - inicio,
            Lidos = lidos,
            Enviados = enviados,
            Rejeitados = rejeitados,
            Resultado = resultado,
        });
    }

    /// <summary>Apaga os arquivos com data anterior a hoje − 30 dias (LOG-06). O host chama ao iniciar e uma vez por dia.</summary>
    public void LimparAntigos()
    {
        var limite = Hoje().AddDays(-DiasRetencao);
        lock (_lock)
        {
            foreach (var arquivo in Directory.GetFiles(_pasta, "coletor-*.jsonl"))
            {
                if (DataDoArquivo(arquivo) is { } data && data < limite)
                {
                    File.Delete(arquivo);
                }
            }
        }
    }

    internal static string NomeArquivo(DateOnly dia) => $"coletor-{dia:yyyyMMdd}.jsonl";

    internal static DateOnly? DataDoArquivo(string caminho) =>
        DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(caminho).Replace("coletor-", string.Empty, StringComparison.Ordinal),
            "yyyyMMdd", out var data) ? data : null;

    private static string Sanitizar(string mensagem) => SenhaNaConnectionString().Replace(mensagem, "$1=***");

    [GeneratedRegex(@"\b(Password|Pwd)\s*=\s*[^;]*", RegexOptions.IgnoreCase)]
    private static partial Regex SenhaNaConnectionString();

    private DateOnly Hoje() => DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime);

    private EventoLog Evento(ContextoExecucao c, NivelLog nivel, string evento) =>
        new(_relogio.GetLocalNow(), nivel, evento, c.ExecucaoId, c.Tenant, c.NomeTenant, c.Instancia, c.Banco, c.Rotina);

    private void Escrever(EventoLog evento)
    {
        var linha = JsonSerializer.Serialize(evento, Json) + Environment.NewLine;
        lock (_lock)
        {
            File.AppendAllText(Path.Combine(_pasta, NomeArquivo(DateOnly.FromDateTime(evento.DataHora.DateTime))), linha);
        }
    }
}
