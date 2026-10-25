using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Coletor.Testes.Unidade.Sincronizacao;

/// <summary>Pausar, retomar e reiniciar pelo <see cref="ComandosConfiguracao"/>: CTL-01, CTL-02, CTL-03, CTL-04, CTL-06.</summary>
[Trait("Categoria", "Unidade")]
public sealed class ComandosConfiguracaoTestes : IAsyncLifetime
{
    private static readonly DateTime Dia = new(2026, 10, 1);

    private readonly PastaTemporaria _pasta = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly InstanciaConexao _instancia = new(Guid.NewGuid(), "SRV-ERP", null, TipoAutenticacao.Windows, null, null);
    private readonly Checkpoints _checkpoints;
    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly ExecutorFalso _executor;
    private readonly ComandosConfiguracao _comandos = new();
    private readonly List<(Guid Id, Checkpoint? Checkpoint, DateOnly? PeriodoInicial)> _vistos = [];
    private readonly SemaphoreSlim _fimDoLote = new(0);
    private Agendador _agendador = null!;
    private int _lotesConcluidos;

    public ComandosConfiguracaoTestes()
    {
        _checkpoints = new Checkpoints(_pasta.Caminho);
        _armazenamento = new ArmazenamentoConfiguracao(_pasta.Caminho, _checkpoints, _relogio);
        _armazenamento.SalvarInstancia(_instancia);
        _executor = new ExecutorFalso(_relogio);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _agendador.StopAsync(CancellationToken.None);
        _agendador.Dispose();
        _pasta.Dispose();
    }

    private ConfiguracaoSincronizacao NovaConfiguracao(Checkpoint? checkpoint = null)
    {
        var id = Guid.NewGuid();
        var configuracao = new ConfiguracaoSincronizacao(id, Guid.NewGuid(), "Cliente", _instancia.Id, $"ERP_{id:N}",
            ConfiguracaoSincronizacao.RotinaVendas, TimeSpan.FromMinutes(5), null, 7, SituacaoConfiguracao.Ativa);
        _armazenamento.SalvarConfiguracao(configuracao);
        if (checkpoint is not null)
        {
            _checkpoints.Avancar(configuracao.Id, configuracao.Rotina, checkpoint);
        }

        return configuracao;
    }

    private ConfiguracaoSincronizacao Gravada(Guid id) => _armazenamento.Carregar().Configuracoes.Single(c => c.Id == id);

    private Checkpoint? CheckpointDe(ConfiguracaoSincronizacao c) => _checkpoints.Obter(c.Id, c.Rotina);

    private IReadOnlyList<(Guid Id, Checkpoint? Checkpoint, DateOnly? PeriodoInicial)> Vistos(Guid id)
    {
        lock (_vistos)
        {
            return [.. _vistos.Where(v => v.Id == id)];
        }
    }

    /// <summary>Cada execução anota o checkpoint e o período que encontrou; com lotes, cada lote espera o teste soltar.</summary>
    private void ExecutorComLotes(bool travarLotes)
    {
        _executor.Comportamento = async (configuracao, parar, ct) =>
        {
            var checkpoint = _checkpoints.Obter(configuracao.Id, configuracao.Rotina);
            lock (_vistos)
            {
                _vistos.Add((configuracao.Id, checkpoint, configuracao.PeriodoInicial));
            }

            if (!travarLotes)
            {
                return ExecutorFalso.Resumo(ResultadoExecucao.Sucesso, _relogio.GetUtcNow());
            }

            for (var lote = 1; lote <= 3; lote++)
            {
                await _fimDoLote.WaitAsync(ct);
                _checkpoints.Avancar(configuracao.Id, configuracao.Rotina, new Checkpoint(Dia, 1000 + lote));
                Interlocked.Increment(ref _lotesConcluidos);
                if (parar())
                {
                    return ExecutorFalso.Resumo(ResultadoExecucao.Cancelada, _relogio.GetUtcNow());
                }
            }

            return ExecutorFalso.Resumo(ResultadoExecucao.Sucesso, _relogio.GetUtcNow());
        };
    }

    private async Task IniciarAsync()
    {
        _agendador = new Agendador(_armazenamento, _checkpoints, _executor, _comandos, _relogio);
        await _agendador.StartAsync(CancellationToken.None);
    }

    private Task ParadaAsync(Guid id) =>
        Esperar.AteAsync(() => _agendador.Estado(id) is { Executando: false, ProximaExecucao: null } && _executor.EmAndamento == 0,
            "laço encerrado");

    [Fact]
    public async Task Pausar_durante_a_execucao_conclui_o_lote_e_nao_agenda_mais()
    {
        var a = NovaConfiguracao();
        ExecutorComLotes(travarLotes: true);
        await IniciarAsync();
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 1, "execução em andamento");

        _comandos.Pausar(a.Id);
        await Esperar.AteAsync(() => Gravada(a.Id).Situacao == SituacaoConfiguracao.Pausada, "situação Pausada");
        Assert.Equal((0, 1), (_lotesConcluidos, _executor.EmAndamento));

        _fimDoLote.Release();
        await ParadaAsync(a.Id);
        Assert.Equal(1, _lotesConcluidos);
        Assert.Equal(new Checkpoint(Dia, 1001), CheckpointDe(a));

        _relogio.Advance(TimeSpan.FromHours(1));
        await Esperar.UmPouco();
        Assert.Single(_executor.Inicios);
    }

    [Fact]
    public async Task Retomar_volta_a_agendar_a_partir_do_checkpoint_preservado()
    {
        var preservado = new Checkpoint(Dia, 777);
        var a = NovaConfiguracao(preservado);
        ExecutorComLotes(travarLotes: false);
        await IniciarAsync();
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 1, "primeira execução");
        _comandos.Pausar(a.Id);
        await ParadaAsync(a.Id);
        _relogio.Advance(TimeSpan.FromMinutes(10));
        await Esperar.UmPouco();
        Assert.Single(_executor.Inicios);

        _comandos.Retomar(a.Id);
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 2, "execução depois de retomar");

        Assert.Equal(SituacaoConfiguracao.Ativa, Gravada(a.Id).Situacao);
        Assert.Equal(preservado, Vistos(a.Id)[1].Checkpoint);
        await Esperar.AteAsync(() => _agendador.Estado(a.Id) is { Executando: false, ProximaExecucao: not null }, "reagendada");
        _relogio.Advance(TimeSpan.FromMinutes(5));
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 3, "volta ao intervalo");
    }

    [Fact]
    public async Task Reiniciar_durante_a_execucao_so_zera_o_checkpoint_depois_do_lote()
    {
        var a = NovaConfiguracao(new Checkpoint(Dia, 500));
        var novoPeriodo = new DateOnly(2026, 1, 1);
        ExecutorComLotes(travarLotes: true);
        await IniciarAsync();
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 1, "execução em andamento");

        _comandos.Reiniciar(a.Id, novoPeriodo);
        await Esperar.UmPouco();
        Assert.Equal(new Checkpoint(Dia, 500), CheckpointDe(a));
        Assert.Null(Gravada(a.Id).PeriodoInicial);

        _fimDoLote.Release();
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 2, "nova carga inicial");

        Assert.Equal(1, _lotesConcluidos);
        Assert.Equal((null, novoPeriodo), (Vistos(a.Id)[1].Checkpoint, Vistos(a.Id)[1].PeriodoInicial));
        Assert.Null(CheckpointDe(a));
    }

    [Fact]
    public async Task Reiniciar_zera_so_a_configuracao_alvo_e_aplica_o_novo_periodo_inicial()
    {
        var a = NovaConfiguracao(new Checkpoint(Dia, 10));
        var b = NovaConfiguracao(new Checkpoint(Dia, 20));
        ExecutorComLotes(travarLotes: false);
        await IniciarAsync();
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 1 && Vistos(b.Id).Count == 1, "primeiras execuções");
        await Esperar.AteAsync(() => _agendador.Estado(a.Id)?.ProximaExecucao is not null, "a reagendada");

        _comandos.Reiniciar(a.Id, new DateOnly(2026, 2, 1));
        await Esperar.AteAsync(() => Vistos(a.Id).Count == 2, "nova carga de a");

        Assert.Equal((null, new DateOnly(2026, 2, 1)), (Vistos(a.Id)[1].Checkpoint, Vistos(a.Id)[1].PeriodoInicial));
        Assert.Equal(new DateOnly(2026, 2, 1), Gravada(a.Id).PeriodoInicial);
        Assert.Equal(new Checkpoint(Dia, 20), CheckpointDe(b));
        Assert.Null(Gravada(b.Id).PeriodoInicial);
        Assert.Single(Vistos(b.Id));

        // Sem período informado, o padrão de 6 meses (CTL-03) é calculado na hora do reinício.
        _comandos.Reiniciar(b.Id);
        await Esperar.AteAsync(() => Vistos(b.Id).Count == 2, "nova carga de b");
        Assert.Equal((null, new DateOnly(2026, 4, 25)), (Vistos(b.Id)[1].Checkpoint, Vistos(b.Id)[1].PeriodoInicial));
    }

    [Fact]
    public async Task Recarregar_inicia_uma_configuracao_ativa_cadastrada_depois()
    {
        var existente = NovaConfiguracao();
        ExecutorComLotes(travarLotes: false);
        await IniciarAsync();
        // O agendador já carregou as configurações (ExecuteAsync roda numa thread de fundo).
        await Esperar.AteAsync(() => Vistos(existente.Id).Count == 1, "configuração existente executada");
        var nova = NovaConfiguracao();
        await Esperar.UmPouco();
        Assert.Empty(Vistos(nova.Id));

        _comandos.Recarregar();

        await Esperar.AteAsync(() => Vistos(nova.Id).Count == 1, "execução da nova configuração");
    }
}
