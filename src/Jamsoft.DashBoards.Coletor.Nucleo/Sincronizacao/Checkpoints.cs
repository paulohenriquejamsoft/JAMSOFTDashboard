using System.Text.Json;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

/// <summary>Último (<c>datafatura</c>, <c>NumNotaSaida</c>) confirmado pela API (SYNC-03).</summary>
public sealed record Checkpoint(DateTime DataFatura, int NumNotaSaida);

/// <summary>
/// <c>checkpoints.json</c>: um checkpoint por (configuração, rotina) (SYNC-09), gravado de forma atômica.
/// Uma instância por pasta (singleton): o lock serializa as execuções paralelas que gravam no mesmo arquivo.
/// </summary>
public sealed class Checkpoints(string pastaDados)
{
    public const string NomeArquivo = "checkpoints.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _caminho = Path.Combine(pastaDados, NomeArquivo);
    private readonly Lock _lock = new();

    public Checkpoint? Obter(Guid configuracaoId, string rotina)
    {
        lock (_lock)
        {
            return Ler().GetValueOrDefault(Chave(configuracaoId, rotina));
        }
    }

    /// <summary>Chamado só depois do ACK do lote (SYNC-03).</summary>
    public void Avancar(Guid configuracaoId, string rotina, Checkpoint checkpoint) =>
        Alterar(todos => todos[Chave(configuracaoId, rotina)] = checkpoint);

    /// <summary>Apaga só a entrada desta (configuração, rotina): reinício (CTL-04) e exclusão (CFG-09).</summary>
    public void Zerar(Guid configuracaoId, string rotina) => Alterar(todos => todos.Remove(Chave(configuracaoId, rotina)));

    private void Alterar(Action<Dictionary<string, Checkpoint>> alteracao)
    {
        lock (_lock)
        {
            var todos = Ler();
            alteracao(todos);
            ArquivoAtomico.Gravar(_caminho, arquivo => JsonSerializer.Serialize(arquivo, todos, Json));
        }
    }

    private Dictionary<string, Checkpoint> Ler()
    {
        if (!File.Exists(_caminho))
        {
            return [];
        }

        using var arquivo = File.OpenRead(_caminho);
        return JsonSerializer.Deserialize<Dictionary<string, Checkpoint>>(arquivo, Json) ?? [];
    }

    private static string Chave(Guid configuracaoId, string rotina) => $"{configuracaoId:D}|{rotina}";
}
