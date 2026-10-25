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
/// a credencial, encerra todos até um <see cref="ComandosConfiguracao.Recarregar"/> depois do novo login.
/// Pausar, retomar e reiniciar chegam por <see cref="ComandosConfiguracao"/> (CTL-01..06).
/// </summary>
public sealed class Agendador : BackgroundService
{
    public const int MaximoSimultaneas = 4;

    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly Checkpoints _checkpoints;
    private readonly IExecutorSincronizacao _executor;
    private readonly ComandosConfiguracao _comandos;
    private readonly TimeProvider _relogio;
    private readonly SemaphoreSlim _semaforo;
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Laco> _lacos = [];
    private readonly Dictionary<Guid, EstadoAgendamento> _estados = [];
    private readonly List<Task> _tarefas = [];
    private CancellationTokenSource _todos = new();

    public Agendador(ArmazenamentoConfiguracao armazenamento, Checkpoints checkpoints, IExecutorSincronizacao executor,
        ComandosConfiguracao comandos, TimeProvider relogio, int maximoSimultaneas = MaximoSimultaneas)
    {
        _armazenamento = armazenamento;
        _checkpoints = checkpoints;
        _executor = executor;
        _comandos = comandos;
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
            return _estados.GetValueOrDefault(configuracaoId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IniciarAtivas(stoppingToken);
        try
        {
            await foreach (var comando in _comandos.Leitor.ReadAllAsync(stoppingToken))
            {
                Aplicar(comando, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }

        await AguardarLacosAsync();
    }

    private void Aplicar(ComandoConfiguracao comando, CancellationToken parada)
    {
        switch (comando)
        {
            case PausarConfiguracao(var id):
                // Gravada antes de avisar o laço: ao reler a configuração ele não começa outra execução.
                MudarSituacao(id, SituacaoConfiguracao.Pausada);
                Avisar(id, laco => laco.PararPedido = true, despertar: true);
                break;

            case RetomarConfiguracao(var id):
                MudarSituacao(id, SituacaoConfiguracao.Ativa);
                if (!Avisar(id, laco => laco.PararPedido = false, despertar: false))
                {
                    Iniciar(id, parada);
                }

                break;

            case ReiniciarConfiguracao(var id, var periodo):
                var novoPeriodo = periodo ?? _armazenamento.PeriodoInicialPadrao();
                // CTL-06: com o laço vivo, quem zera é ele, depois do lote em andamento.
                if (!Avisar(id, laco =>
                {
                    laco.ReinicioPendente = novoPeriodo;
                    laco.PararPedido = true;
                }, despertar: true))
                {
                    Reiniciar(id, novoPeriodo);
                    Iniciar(id, parada);
                }

                break;

            case RecarregarConfiguracoes:
                IniciarAtivas(parada);
                break;
        }
    }

    /// <summary>
    /// Aplica <paramref name="acao"/> ao laço vivo da configuração e, com <paramref name="despertar"/>, interrompe a espera
    /// do intervalo (fora do lock). Falso quando não há laço.
    /// </summary>
    private bool Avisar(Guid id, Action<Laco> acao, bool despertar)
    {
        CancellationTokenSource? espera;
        lock (_lock)
        {
            if (!_lacos.TryGetValue(id, out var laco))
            {
                return false;
            }

            acao(laco);
            espera = despertar ? laco.Despertar : null;
        }

        espera?.Cancel();
        return true;
    }

    private void MudarSituacao(Guid id, SituacaoConfiguracao situacao)
    {
        if (_armazenamento.Carregar().Configuracoes.FirstOrDefault(c => c.Id == id) is { } configuracao && configuracao.Situacao != situacao)
        {
            _armazenamento.SalvarConfiguracao(configuracao with { Situacao = situacao });
        }
    }

    /// <summary>CTL-04: zera só o checkpoint desta configuração e grava o novo período inicial.</summary>
    private void Reiniciar(Guid id, DateOnly novoPeriodo)
    {
        if (_armazenamento.Carregar().Configuracoes.FirstOrDefault(c => c.Id == id) is { } configuracao)
        {
            _checkpoints.Zerar(configuracao.Id, configuracao.Rotina);
            _armazenamento.SalvarConfiguracao(configuracao with { PeriodoInicial = novoPeriodo });
        }
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
            if (_lacos.ContainsKey(configuracaoId))
            {
                return;
            }

            var laco = new Laco(configuracaoId, CancellationTokenSource.CreateLinkedTokenSource(parada, _todos.Token));
            _lacos[configuracaoId] = laco;
            _tarefas.Add(Task.Run(() => LacoAsync(laco)));
        }
    }

    private async Task AguardarLacosAsync()
    {
        Task[] tarefas;
        lock (_lock)
        {
            tarefas = [.. _tarefas];
        }

        await Task.WhenAll(tarefas);
    }

    private async Task LacoAsync(Laco laco)
    {
        try
        {
            await CiclosAsync(laco);
        }
        finally
        {
            lock (_lock)
            {
                Sair(laco);
            }
        }
    }

    /// <summary>Tira o laço do mapa (chamado com <see cref="_lock"/>): um comando depois disso inicia outro.</summary>
    private void Sair(Laco laco)
    {
        if (_lacos.GetValueOrDefault(laco.ConfiguracaoId) == laco)
        {
            _lacos.Remove(laco.ConfiguracaoId);
        }
    }

    private async Task CiclosAsync(Laco laco)
    {
        var ct = laco.Cancelamento.Token;
        while (!ct.IsCancellationRequested)
        {
            CancellationToken despertar;
            DateOnly? reinicio;
            lock (_lock)
            {
                reinicio = laco.ReinicioPendente;
                laco.ReinicioPendente = null;
                laco.PararPedido = false;
                laco.Despertar = new CancellationTokenSource();
                despertar = laco.Despertar.Token;
            }

            if (reinicio is { } novoPeriodo)
            {
                Reiniciar(laco.ConfiguracaoId, novoPeriodo);
            }

            // Relida a cada ciclo: edições, pausa e exclusão feitas na tela valem a partir do próximo. Ler e sair
            // juntos, sob o lock: um "retomar" logo depois encontra o mapa sem este laço e inicia outro.
            ConfiguracaoSincronizacao? configuracao;
            lock (_lock)
            {
                configuracao = _armazenamento.Carregar().Configuracoes.FirstOrDefault(c => c.Id == laco.ConfiguracaoId);
                if (configuracao is not { Situacao: SituacaoConfiguracao.Ativa })
                {
                    Sair(laco);
                }
            }

            if (configuracao is not { Situacao: SituacaoConfiguracao.Ativa })
            {
                Notificar(laco, new EstadoAgendamento(laco.ConfiguracaoId, false, null, UltimoResumo(laco.ConfiguracaoId)));
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
                Notificar(laco, new EstadoAgendamento(laco.ConfiguracaoId, true, null, UltimoResumo(laco.ConfiguracaoId)));
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
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct, despertar);
            var aguardar = Task.Delay(configuracao.Intervalo, _relogio, espera.Token);
            // Avisa depois de armar a espera: quem vê a próxima execução já encontra o timer contando.
            Notificar(laco, new EstadoAgendamento(laco.ConfiguracaoId, false, proxima, resumo));
            try
            {
                await aguardar;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Despertado por um comando: o próximo ciclo relê a configuração e aplica o reinício pendente.
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

    private ResumoExecucao? UltimoResumo(Guid configuracaoId)
    {
        lock (_lock)
        {
            return _estados.GetValueOrDefault(configuracaoId)?.UltimoResumo;
        }
    }

    private void Notificar(Laco laco, EstadoAgendamento estado)
    {
        lock (_lock)
        {
            _estados[laco.ConfiguracaoId] = estado;
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
        public bool PararPedido { get; set; }
        public DateOnly? ReinicioPendente { get; set; }
        public CancellationTokenSource Despertar { get; set; } = new();
    }
}
