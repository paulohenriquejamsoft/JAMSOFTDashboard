using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Microsoft.Extensions.Hosting;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

/// <summary>Mudança no agendamento de uma configuração, para a tela principal atualizar a linha (CFG-10).</summary>
/// <param name="UltimoResumo">Preenchido quando uma execução acabou de terminar.</param>
public sealed record EstadoAgendamento(Guid ConfiguracaoId, bool Executando, DateTimeOffset? ProximaExecucao, ResumoExecucao? UltimoResumo);

/// <summary>
/// Um laço por configuração ativa (SCH-01..05): executa ao iniciar (retomando do checkpoint, SCH-05) e depois a cada
/// intervalo contado do fim da execução anterior (SCH-01). Um único laço por configuração impede duas execuções da
/// mesma (SCH-02); um semáforo global limita a <see cref="MaximoSimultaneas"/> execuções (SCH-03); a falha de uma
/// configuração só afeta o laço dela (SCH-04). A API recusando o tenant encerra o laço daquela configuração; recusando
/// a credencial, encerra todos até um novo login.
/// </summary>
public sealed class Agendador : BackgroundService
{
    public const int MaximoSimultaneas = 4;

    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly IExecutorSincronizacao _executor;
    private readonly TimeProvider _relogio;
    private readonly SemaphoreSlim _semaforo;
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Laco> _lacos = [];
    private CancellationTokenSource _todos = new();

    public Agendador(ArmazenamentoConfiguracao armazenamento, IExecutorSincronizacao executor, TimeProvider relogio,
        int maximoSimultaneas = MaximoSimultaneas)
    {
        _armazenamento = armazenamento;
        _executor = executor;
        _relogio = relogio;
        _semaforo = new SemaphoreSlim(maximoSimultaneas, maximoSimultaneas);
    }

    /// <summary>Disparado a cada início e fim de execução e a cada reagendamento (em outra thread).</summary>
    public event EventHandler<EstadoAgendamento>? EstadoAlterado;

    /// <summary>A API recusou a credencial do Coletor: todas as configurações pararam até um novo login.</summary>
    public event EventHandler? CredencialRecusada;

    public EstadoAgendamento? Estado(Guid configuracaoId)
    {
        lock (_lock)
        {
            return _lacos.GetValueOrDefault(configuracaoId)?.Estado;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IniciarAtivas(stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }

        await AguardarLacosAsync();
    }

    private void IniciarAtivas(CancellationToken parada)
    {
        foreach (var configuracao in _armazenamento.Carregar().Configuracoes.Where(c => c.Situacao == SituacaoConfiguracao.Ativa))
        {
            Iniciar(configuracao.Id, parada);
        }
    }

    private void Iniciar(Guid configuracaoId, CancellationToken parada)
    {
        lock (_lock)
        {
            if (_lacos.TryGetValue(configuracaoId, out var existente) && !existente.Tarefa.IsCompleted)
            {
                return;
            }

            var laco = new Laco(configuracaoId, CancellationTokenSource.CreateLinkedTokenSource(parada, _todos.Token));
            _lacos[configuracaoId] = laco;
            laco.Tarefa = Task.Run(() => LacoAsync(laco));
        }
    }

    private async Task AguardarLacosAsync()
    {
        Task[] tarefas;
        lock (_lock)
        {
            tarefas = [.. _lacos.Values.Select(l => l.Tarefa)];
        }

        await Task.WhenAll(tarefas);
    }

    private async Task LacoAsync(Laco laco)
    {
        var ct = laco.Cancelamento.Token;
        while (!ct.IsCancellationRequested)
        {
            // Relida a cada ciclo: edições, pausa e exclusão feitas na tela valem a partir do próximo.
            var configuracao = _armazenamento.Carregar().Configuracoes.FirstOrDefault(c => c.Id == laco.ConfiguracaoId);
            if (configuracao is not { Situacao: SituacaoConfiguracao.Ativa })
            {
                break;
            }

            ResumoExecucao? resumo = null;
            try
            {
                await _semaforo.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                Notificar(laco, new EstadoAgendamento(laco.ConfiguracaoId, true, null, null));
                resumo = await _executor.ExecutarAsync(configuracao, () => laco.PararPedido, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // SCH-04: o executor classifica e registra os erros; algo inesperado não derruba este laço nem os outros.
            }
            finally
            {
                _semaforo.Release();
            }

            if (resumo?.Recusa == SituacaoResposta.CredencialInvalida)
            {
                Notificar(laco, new EstadoAgendamento(laco.ConfiguracaoId, false, null, resumo));
                PararTodos();
                break;
            }

            if (resumo?.Recusa == SituacaoResposta.NaoAutorizada)
            {
                Notificar(laco, new EstadoAgendamento(laco.ConfiguracaoId, false, null, resumo));
                break;
            }

            var proxima = _relogio.GetUtcNow() + configuracao.Intervalo;
            Notificar(laco, new EstadoAgendamento(laco.ConfiguracaoId, false, proxima, resumo));
            try
            {
                await Task.Delay(configuracao.Intervalo, _relogio, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void PararTodos()
    {
        CancellationTokenSource anterior;
        lock (_lock)
        {
            anterior = _todos;
            _todos = new CancellationTokenSource();
        }

        anterior.Cancel();
        CredencialRecusada?.Invoke(this, EventArgs.Empty);
    }

    private void Notificar(Laco laco, EstadoAgendamento estado)
    {
        lock (_lock)
        {
            laco.Estado = estado;
        }

        EstadoAlterado?.Invoke(this, estado);
    }

    public override void Dispose()
    {
        base.Dispose();
        _semaforo.Dispose();
        _todos.Dispose();
    }

    private sealed class Laco(Guid configuracaoId, CancellationTokenSource cancelamento)
    {
        public Guid ConfiguracaoId { get; } = configuracaoId;
        public CancellationTokenSource Cancelamento { get; } = cancelamento;
        public Task Tarefa { get; set; } = Task.CompletedTask;
        public EstadoAgendamento? Estado { get; set; }
        public bool PararPedido { get; set; }
    }
}
