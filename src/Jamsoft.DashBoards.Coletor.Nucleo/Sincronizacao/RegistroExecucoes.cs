using System.Text.Json;
using System.Text.Json.Serialization;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

/// <summary>
/// O que a tela principal mostra de cada configuração (CFG-10): último sucesso (SYNC-06), resultado e mensagem da
/// última execução e se a API recusou o tenant ("Não autorizada").
/// </summary>
public sealed record EstadoExecucao(
    DateTimeOffset? UltimoSucessoEm,
    ResultadoExecucao? UltimoResultado,
    DateTimeOffset? UltimaExecucaoFim,
    string? UltimaMensagem,
    bool NaoAutorizada);

/// <summary>
/// <c>execucoes.json</c>: o <see cref="EstadoExecucao"/> por configuração, gravado de forma atômica ao fim de cada
/// execução. Uma instância por pasta (singleton).
/// </summary>
// SPEC_DEVIATION: o design guarda ultimoSucessoEm e situação no checkpoints.json.
// Reason: reiniciar (CTL-04) apaga o checkpoint; separado, o último sucesso continua visível na tela depois do reinício.
public sealed class RegistroExecucoes(string pastaDados)
{
    public const string NomeArquivo = "execucoes.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _caminho = Path.Combine(pastaDados, NomeArquivo);
    private readonly Lock _lock = new();

    public EstadoExecucao? Obter(Guid configuracaoId)
    {
        lock (_lock)
        {
            return Ler().GetValueOrDefault(configuracaoId);
        }
    }

    /// <summary>Fim de uma execução: o último sucesso só muda quando o resultado é Sucesso (SYNC-06).</summary>
    public void Registrar(Guid configuracaoId, ResultadoExecucao resultado, DateTimeOffset fim, string? mensagem, bool naoAutorizada)
    {
        lock (_lock)
        {
            var todos = Ler();
            var anterior = todos.GetValueOrDefault(configuracaoId);
            todos[configuracaoId] = new EstadoExecucao(
                resultado == ResultadoExecucao.Sucesso ? fim : anterior?.UltimoSucessoEm, resultado, fim, mensagem, naoAutorizada);
            ArquivoAtomico.Gravar(_caminho, arquivo => JsonSerializer.Serialize(arquivo, todos, Json));
        }
    }

    private Dictionary<Guid, EstadoExecucao> Ler()
    {
        if (!File.Exists(_caminho))
        {
            return [];
        }

        using var arquivo = File.OpenRead(_caminho);
        return JsonSerializer.Deserialize<Dictionary<Guid, EstadoExecucao>>(arquivo, Json) ?? [];
    }
}
