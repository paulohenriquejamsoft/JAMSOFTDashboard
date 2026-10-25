using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

namespace Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

/// <summary>
/// <see cref="IExecutorSincronizacao"/> falso para os testes do agendador: grava cada início (configuração e hora no
/// relógio falso), mede a concorrência e, com <see cref="Travar"/>, segura cada execução até o teste chamar
/// <see cref="Liberar"/>. <see cref="Comportamento"/> roda dentro da execução (depois da trava).
/// </summary>
public sealed class ExecutorFalso(TimeProvider relogio) : IExecutorSincronizacao
{
    private readonly Lock _lock = new();
    private readonly List<(Guid Id, DateTimeOffset Em)> _inicios = [];
    private readonly List<(Guid Id, TaskCompletionSource Trava)> _travadas = [];
    private readonly Dictionary<Guid, int> _porConfiguracao = [];
    private int _emAndamento;

    public bool Travar { get; set; }

    public Func<ConfiguracaoSincronizacao, Func<bool>, CancellationToken, Task<ResumoExecucao>>? Comportamento { get; set; }

    public int MaximoSimultaneo { get; private set; }

    /// <summary>Alguma configuração teve duas execuções ao mesmo tempo.</summary>
    public bool Sobreposicao { get; private set; }

    public int EmAndamento
    {
        get
        {
            lock (_lock)
            {
                return _emAndamento;
            }
        }
    }

    public IReadOnlyList<(Guid Id, DateTimeOffset Em)> Inicios
    {
        get
        {
            lock (_lock)
            {
                return [.. _inicios];
            }
        }
    }

    public IReadOnlyList<DateTimeOffset> IniciosDe(Guid id) => [.. Inicios.Where(i => i.Id == id).Select(i => i.Em)];

    public static ResumoExecucao Resumo(ResultadoExecucao resultado, DateTimeOffset em) => new(Guid.NewGuid(), resultado, em, em, 0, 0, 0);

    /// <summary>Solta a execução travada mais antiga da configuração.</summary>
    public void Liberar(Guid id)
    {
        TaskCompletionSource trava;
        lock (_lock)
        {
            var indice = _travadas.FindIndex(t => t.Id == id);
            trava = _travadas[indice].Trava;
            _travadas.RemoveAt(indice);
        }

        trava.SetResult();
    }

    public async Task<ResumoExecucao> ExecutarAsync(ConfiguracaoSincronizacao configuracao, Func<bool> pararEntreLotes, CancellationToken ct)
    {
        TaskCompletionSource? trava = null;
        lock (_lock)
        {
            _inicios.Add((configuracao.Id, relogio.GetUtcNow()));
            _emAndamento++;
            MaximoSimultaneo = Math.Max(MaximoSimultaneo, _emAndamento);
            _porConfiguracao[configuracao.Id] = _porConfiguracao.GetValueOrDefault(configuracao.Id) + 1;
            Sobreposicao |= _porConfiguracao[configuracao.Id] > 1;
            if (Travar)
            {
                trava = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _travadas.Add((configuracao.Id, trava));
            }
        }

        try
        {
            if (trava is not null)
            {
                await trava.Task.WaitAsync(ct);
            }

            return Comportamento is null
                ? Resumo(ResultadoExecucao.Sucesso, relogio.GetUtcNow())
                : await Comportamento(configuracao, pararEntreLotes, ct);
        }
        finally
        {
            lock (_lock)
            {
                _emAndamento--;
                _porConfiguracao[configuracao.Id]--;
            }
        }
    }
}

/// <summary>Espera, em tempo real, uma condição que outra thread vai tornar verdadeira.</summary>
public static class Esperar
{
    public static async Task AteAsync(Func<bool> condicao, string descricao, int milissegundos = 10_000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(milissegundos);
        while (!condicao())
        {
            if (DateTime.UtcNow > limite)
            {
                throw new TimeoutException($"Esperando: {descricao}");
            }

            await Task.Delay(10);
        }

        // Dá tempo do laço que mudou o estado chegar à próxima espera no relógio falso.
        await Task.Delay(30);
    }

    /// <summary>Tempo real para confirmar que nada mais aconteceu.</summary>
    public static Task UmPouco() => Task.Delay(150);
}
