using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Coletor.Testes.Unidade.Sincronizacao;

/// <summary><see cref="Agendador"/> com executor falso e relógio falso: SCH-01..SCH-05.</summary>
[Trait("Categoria", "Unidade")]
public sealed class AgendadorTestes : IAsyncLifetime
{
    private static readonly TimeSpan Cinco = TimeSpan.FromMinutes(5);

    private readonly PastaTemporaria _pasta = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly InstanciaConexao _instancia = new(Guid.NewGuid(), "SRV-ERP", null, TipoAutenticacao.Windows, null, null);
    private readonly List<Agendador> _agendadores = [];
    private readonly Checkpoints _checkpoints;
    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly ExecutorFalso _executor;

    public AgendadorTestes()
    {
        _checkpoints = new Checkpoints(_pasta.Caminho);
        _armazenamento = new ArmazenamentoConfiguracao(_pasta.Caminho, _checkpoints, _relogio);
        _armazenamento.SalvarInstancia(_instancia);
        _executor = new ExecutorFalso(_relogio);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var agendador in _agendadores)
        {
            await agendador.StopAsync(CancellationToken.None);
            agendador.Dispose();
        }

        _pasta.Dispose();
    }

    private ConfiguracaoSincronizacao NovaConfiguracao(SituacaoConfiguracao situacao = SituacaoConfiguracao.Ativa, TimeSpan? intervalo = null)
    {
        var id = Guid.NewGuid();
        var configuracao = new ConfiguracaoSincronizacao(id, Guid.NewGuid(), "Cliente", _instancia.Id, $"ERP_{id:N}",
            ConfiguracaoSincronizacao.RotinaVendas, intervalo ?? Cinco, null, 7, situacao);
        _armazenamento.SalvarConfiguracao(configuracao);
        return configuracao;
    }

    private async Task<Agendador> IniciarAsync(IExecutorSincronizacao? executor = null, ArmazenamentoConfiguracao? armazenamento = null)
    {
        var agendador = new Agendador(armazenamento ?? _armazenamento, executor ?? _executor, _relogio);
        _agendadores.Add(agendador);
        await agendador.StartAsync(CancellationToken.None);
        return agendador;
    }

    private static Task AgendadaAsync(Agendador agendador, Guid id) =>
        Esperar.AteAsync(() => agendador.Estado(id) is { Executando: false, ProximaExecucao: not null }, $"{id} reagendada");

    [Fact]
    public async Task Intervalo_e_contado_a_partir_do_fim_da_execucao_anterior()
    {
        var a = NovaConfiguracao(intervalo: Cinco);
        _executor.Travar = true;
        var inicio = _relogio.GetUtcNow();
        var agendador = await IniciarAsync();
        await Esperar.AteAsync(() => _executor.Inicios.Count == 1, "primeira execução");

        _relogio.Advance(TimeSpan.FromMinutes(3));
        _executor.Liberar(a.Id);
        await AgendadaAsync(agendador, a.Id);
        _relogio.Advance(Cinco - TimeSpan.FromSeconds(1));
        await Esperar.UmPouco();
        Assert.Single(_executor.Inicios);

        _relogio.Advance(TimeSpan.FromSeconds(1));
        await Esperar.AteAsync(() => _executor.Inicios.Count == 2, "segunda execução");

        Assert.Equal([inicio, inicio + TimeSpan.FromMinutes(8)], _executor.IniciosDe(a.Id));
    }

    [Fact]
    public async Task Nunca_executa_duas_vezes_a_mesma_configuracao_ao_mesmo_tempo()
    {
        var a = NovaConfiguracao(intervalo: Cinco);
        _executor.Travar = true;
        var agendador = await IniciarAsync();
        await Esperar.AteAsync(() => _executor.Inicios.Count == 1, "primeira execução");

        // A execução passa de vários intervalos: nenhuma outra começa enquanto ela não termina.
        _relogio.Advance(TimeSpan.FromMinutes(23));
        await Esperar.UmPouco();
        Assert.Single(_executor.Inicios);

        _executor.Liberar(a.Id);
        await AgendadaAsync(agendador, a.Id);
        _relogio.Advance(Cinco);
        await Esperar.AteAsync(() => _executor.Inicios.Count == 2, "segunda execução");

        Assert.False(_executor.Sobreposicao);
        Assert.Equal(1, _executor.MaximoSimultaneo);
    }

    [Fact]
    public async Task Seis_configuracoes_executam_no_maximo_quatro_ao_mesmo_tempo()
    {
        var ativas = Enumerable.Range(0, 6).Select(_ => NovaConfiguracao()).ToList();
        var inativa = NovaConfiguracao(SituacaoConfiguracao.Inativa);
        var pausada = NovaConfiguracao(SituacaoConfiguracao.Pausada);
        _executor.Travar = true;
        await IniciarAsync();

        await Esperar.AteAsync(() => _executor.Inicios.Count == 4, "quatro execuções");
        await Esperar.UmPouco();
        Assert.Equal(4, _executor.Inicios.Count);
        Assert.Equal(4, _executor.EmAndamento);

        _executor.Liberar(_executor.Inicios[0].Id);
        await Esperar.AteAsync(() => _executor.Inicios.Count == 5, "quinta execução");
        _executor.Liberar(_executor.Inicios[1].Id);
        await Esperar.AteAsync(() => _executor.Inicios.Count == 6, "sexta execução");

        Assert.Equal(4, _executor.MaximoSimultaneo);
        Assert.Equal(ativas.Select(c => c.Id).Order(), _executor.Inicios.Select(i => i.Id).Order());
        Assert.DoesNotContain(_executor.Inicios, i => i.Id == inativa.Id || i.Id == pausada.Id);
    }

    [Fact]
    public async Task Falha_de_uma_configuracao_nao_afeta_as_outras()
    {
        var falha = NovaConfiguracao();
        var excecao = NovaConfiguracao();
        var boa = NovaConfiguracao();
        _executor.Comportamento = (configuracao, _, _) =>
        {
            if (configuracao.Id == excecao.Id)
            {
                throw new InvalidOperationException("erro inesperado");
            }

            return Task.FromResult(ExecutorFalso.Resumo(
                configuracao.Id == falha.Id ? ResultadoExecucao.Falha : ResultadoExecucao.Sucesso, _relogio.GetUtcNow()));
        };
        var agendador = await IniciarAsync();
        foreach (var id in new[] { falha.Id, excecao.Id, boa.Id })
        {
            await AgendadaAsync(agendador, id);
        }

        _relogio.Advance(Cinco);
        await Esperar.AteAsync(() => _executor.Inicios.Count == 6, "segundo ciclo das três");

        Assert.All(new[] { falha, excecao, boa }, c => Assert.Equal(2, _executor.IniciosDe(c.Id).Count));
        Assert.Equal(ResultadoExecucao.Sucesso, agendador.Estado(boa.Id)!.UltimoResumo!.Resultado);
    }

    [Fact]
    public async Task Reinicio_do_agendador_retoma_cada_configuracao_do_checkpoint_gravado()
    {
        var a = NovaConfiguracao();
        var vistos = new List<Checkpoint?>();
        IExecutorSincronizacao ExecutorQueAvanca(Checkpoints checkpoints)
        {
            var executor = new ExecutorFalso(_relogio);
            executor.Comportamento = (configuracao, _, _) =>
            {
                var atual = checkpoints.Obter(configuracao.Id, configuracao.Rotina);
                lock (vistos)
                {
                    vistos.Add(atual);
                }

                // Simula o ACK de um lote: o checkpoint avança uma nota.
                checkpoints.Avancar(configuracao.Id, configuracao.Rotina, new Checkpoint(new DateTime(2026, 10, 1), (atual?.NumNotaSaida ?? 0) + 1));
                return Task.FromResult(ExecutorFalso.Resumo(ResultadoExecucao.Sucesso, _relogio.GetUtcNow()));
            };
            return executor;
        }

        var primeiro = await IniciarAsync(ExecutorQueAvanca(_checkpoints));
        await AgendadaAsync(primeiro, a.Id);
        await primeiro.StopAsync(CancellationToken.None);

        // Novo processo: outras instâncias lendo a mesma pasta de dados, sem esperar o intervalo.
        var checkpointsRelidos = new Checkpoints(_pasta.Caminho);
        var segundo = await IniciarAsync(ExecutorQueAvanca(checkpointsRelidos),
            new ArmazenamentoConfiguracao(_pasta.Caminho, checkpointsRelidos, _relogio));
        await AgendadaAsync(segundo, a.Id);

        Assert.Equal([null, new Checkpoint(new DateTime(2026, 10, 1), 1)], vistos);
    }

    [Fact]
    public async Task Tenant_recusado_para_so_a_configuracao_e_credencial_recusada_para_todas()
    {
        var naoAutorizada = NovaConfiguracao();
        var outra = NovaConfiguracao();
        var recusarCredencial = false;
        _executor.Comportamento = (configuracao, _, _) => Task.FromResult(ExecutorFalso.Resumo(ResultadoExecucao.Falha, _relogio.GetUtcNow()) with
        {
            Recusa = configuracao.Id == naoAutorizada.Id ? SituacaoResposta.NaoAutorizada
                : recusarCredencial ? SituacaoResposta.CredencialInvalida : null,
        });
        var agendador = await IniciarAsync();
        var credencialRecusada = false;
        agendador.CredencialRecusada += (_, _) => credencialRecusada = true;
        await AgendadaAsync(agendador, outra.Id);

        _relogio.Advance(Cinco);
        await Esperar.AteAsync(() => _executor.IniciosDe(outra.Id).Count == 2, "segundo ciclo da outra");
        Assert.Single(_executor.IniciosDe(naoAutorizada.Id));

        recusarCredencial = true;
        await AgendadaAsync(agendador, outra.Id);
        _relogio.Advance(Cinco);
        await Esperar.AteAsync(() => credencialRecusada, "credencial recusada");
        _relogio.Advance(Cinco * 3);
        await Esperar.UmPouco();

        Assert.Equal(3, _executor.IniciosDe(outra.Id).Count);
        Assert.Single(_executor.IniciosDe(naoAutorizada.Id));
    }
}
