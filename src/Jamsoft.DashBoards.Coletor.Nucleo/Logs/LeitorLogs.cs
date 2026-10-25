using System.Text.Json;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Logs;

/// <summary>
/// Filtros da tela de logs (LOG-05). <see cref="De"/>/<see cref="Ate"/> são inclusivos. <see cref="Resultado"/>
/// traz todos os eventos das execuções que terminaram com esse resultado.
/// </summary>
public sealed record FiltroLogs(
    DateOnly De,
    DateOnly Ate,
    Guid? Tenant = null,
    string? Banco = null,
    string? Rotina = null,
    ResultadoExecucao? Resultado = null);

/// <summary>Lê os arquivos <c>coletor-AAAAMMDD.jsonl</c> do período e aplica os filtros (LOG-05).</summary>
public sealed class LeitorLogs(string pastaLogs)
{
    public IReadOnlyList<EventoLog> Ler(FiltroLogs filtro)
    {
        if (!Directory.Exists(pastaLogs))
        {
            return [];
        }

        var eventos = Directory.GetFiles(pastaLogs, "coletor-*.jsonl")
            .Select(arquivo => (arquivo, data: LogExecucoes.DataDoArquivo(arquivo)))
            .Where(a => a.data >= filtro.De && a.data <= filtro.Ate)
            .OrderBy(a => a.data)
            .SelectMany(a => LerLinhas(a.arquivo))
            .Where(e => filtro.Tenant is null || e.Tenant == filtro.Tenant)
            .Where(e => filtro.Banco is null || string.Equals(e.Banco, filtro.Banco, StringComparison.OrdinalIgnoreCase))
            .Where(e => filtro.Rotina is null || string.Equals(e.Rotina, filtro.Rotina, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtro.Resultado is { } resultado)
        {
            var execucoes = eventos.Where(e => e.Resultado == resultado).Select(e => e.ExecucaoId).ToHashSet();
            eventos = [.. eventos.Where(e => execucoes.Contains(e.ExecucaoId))];
        }

        return eventos;
    }

    private static IEnumerable<EventoLog> LerLinhas(string arquivo)
    {
        // O arquivo pode estar recebendo linhas de uma execução em andamento.
        using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leitor = new StreamReader(fluxo);
        var eventos = new List<EventoLog>();
        while (leitor.ReadLine() is { } linha)
        {
            if (!string.IsNullOrWhiteSpace(linha))
            {
                eventos.Add(JsonSerializer.Deserialize<EventoLog>(linha, LogExecucoes.Json)!);
            }
        }

        return eventos;
    }
}
