using Dapper;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Coletor.Testes.Integracao.Sincronizacao;

/// <summary>
/// <see cref="ExecutorSincronizacao"/> com o ERP falso e um cliente da API falso: SYNC-01, SYNC-03..06, SYNC-08,
/// CFG-07, LOG-02, LOG-03.
/// </summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ExecutorSincronizacaoTestes(BancoDeTestes banco) : IAsyncLifetime, IDisposable
{
    private readonly PastaTemporaria _pasta = new();
    private readonly FakeTimeProvider _relogio = CriarRelogio();
    private readonly InstanciaConexao _instancia = new(Guid.NewGuid(), "SRV-ERP", 1433, TipoAutenticacao.Windows, null, null);
    private ErpFalso _erp = null!;
    private Checkpoints _checkpoints = null!;
    private RegistroExecucoes _registro = null!;
    private LogExecucoes _log = null!;
    private ArmazenamentoConfiguracao _armazenamento = null!;
    private ClienteApiFalso _cliente = null!;
    private TimeSpan? _timeoutErp;

    private DateTime Hoje => _relogio.GetLocalNow().Date;

    private static FakeTimeProvider CriarRelogio()
    {
        // Mesmo "agora" e mesmo fuso do servidor do ERP de testes (a máquina local).
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        relogio.SetLocalTimeZone(TimeZoneInfo.Local);
        return relogio;
    }

    public async Task InitializeAsync()
    {
        _erp = await ErpFalso.CriarAsync(banco);
        _checkpoints = new Checkpoints(_pasta.Caminho);
        _registro = new RegistroExecucoes(_pasta.Caminho);
        _log = new LogExecucoes(_pasta.Arquivo("logs"), _relogio);
        _armazenamento = new ArmazenamentoConfiguracao(_pasta.Caminho, _checkpoints, _relogio);
        _armazenamento.SalvarInstancia(_instancia);
        _cliente = new ClienteApiFalso(_checkpoints);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _pasta.Dispose();

    private ConfiguracaoSincronizacao NovaConfiguracao(int janela = 7, DateOnly? periodoInicial = null)
    {
        var configuracao = new ConfiguracaoSincronizacao(Guid.NewGuid(), Guid.NewGuid(), "Cliente A", _instancia.Id, _erp.NomeBanco,
            ConfiguracaoSincronizacao.RotinaVendas, ConfiguracaoSincronizacao.IntervaloPadrao, periodoInicial, janela,
            SituacaoConfiguracao.Ativa);
        _armazenamento.SalvarConfiguracao(configuracao);
        return configuracao;
    }

    private Task<ResumoExecucao> ExecutarAsync(ConfiguracaoSincronizacao configuracao, Func<bool>? parar = null) =>
        new ExecutorSincronizacao(_armazenamento, _checkpoints, _registro, _log, _cliente,
                (_, _) => _erp.Extrator(_timeoutErp), _relogio)
            .ExecutarAsync(configuracao, parar ?? (() => false), CancellationToken.None);

    private Checkpoint? CheckpointDe(ConfiguracaoSincronizacao configuracao) => _checkpoints.Obter(configuracao.Id, configuracao.Rotina);

    /// <summary>Todas as notas do ERP falso na ordem do keyset, lidas direto da tabela.</summary>
    private async Task<List<Checkpoint>> NotasEmOrdemAsync()
    {
        await using var conexao = new SqlConnection(_erp.ConnectionString);
        return [.. await conexao.QueryAsync<Checkpoint>(
            "SELECT datafatura AS DataFatura, NumNotaSaida FROM dbo.NotaSaida ORDER BY datafatura, NumNotaSaida;")];
    }

    [Fact]
    public async Task Carga_inicial_le_desde_hoje_menos_6_meses_ate_agora()
    {
        var inicio = Hoje.AddMonths(-6);
        await _erp.InserirAsync(new NotaFalsa(inicio.AddDays(-1)));
        var noInicio = await _erp.InserirAsync(new NotaFalsa(inicio));
        var ontem = await _erp.InserirAsync(new NotaFalsa(Hoje.AddDays(-1)));
        var configuracao = NovaConfiguracao(periodoInicial: null);

        var resumo = await ExecutarAsync(configuracao);

        Assert.Equal(ResultadoExecucao.Sucesso, resumo.Resultado);
        Assert.Equal([noInicio, ontem], _cliente.VendasEnviadas);
        Assert.Equal(new Checkpoint(Hoje.AddDays(-1), ontem), CheckpointDe(configuracao));
    }

    [Fact]
    public async Task Checkpoint_so_avanca_depois_do_ACK_de_cada_lote()
    {
        await _erp.SemearAsync(1200, Hoje.AddDays(-170), dias: 160);
        var notas = await NotasEmOrdemAsync();
        var configuracao = NovaConfiguracao();

        await ExecutarAsync(configuracao);

        Assert.Equal([500, 500, 200], _cliente.Lotes.Select(l => l.Lote.Vendas.Count));
        Assert.Equal([null, notas[499], notas[999]], _cliente.Lotes.Select(l => l.CheckpointNoEnvio));
        Assert.Equal(notas[1199], CheckpointDe(configuracao));
    }

    [Fact]
    public async Task Falha_no_segundo_lote_mantem_o_checkpoint_do_primeiro_e_o_proximo_ciclo_reenvia_a_partir_dele()
    {
        await _erp.SemearAsync(1200, Hoje.AddDays(-170), dias: 160);
        var notas = await NotasEmOrdemAsync();
        var configuracao = NovaConfiguracao(janela: 0);
        _cliente.Responder = (lote, numero) => numero == 2 ? ClienteApiFalso.ApiForaDoAr() : ClienteApiFalso.Ack(lote);

        var falha = await ExecutarAsync(configuracao);

        Assert.Equal((ResultadoExecucao.Falha, TipoErro.Comunicacao), (falha.Resultado, falha.TipoErro));
        Assert.Equal(notas[499], CheckpointDe(configuracao));

        _cliente.Limpar();
        _cliente.Responder = null;
        var proximo = await ExecutarAsync(configuracao);

        Assert.Equal(ResultadoExecucao.Sucesso, proximo.Resultado);
        Assert.Equal(notas.Where(n => n.DataFatura >= notas[499].DataFatura).Select(n => n.NumNotaSaida), _cliente.VendasEnviadas);
        Assert.Equal(notas[1199], CheckpointDe(configuracao));
    }

    [Fact]
    public async Task Incremental_comeca_no_checkpoint_menos_a_janela()
    {
        var dia = Hoje.AddDays(-10);
        await _erp.InserirAsync(new NotaFalsa(dia.AddDays(-8)));
        var limiteDaJanela = await _erp.InserirAsync(new NotaFalsa(dia.AddDays(-7)));
        var doCheckpoint = await _erp.InserirAsync(new NotaFalsa(dia));
        var nova = await _erp.InserirAsync(new NotaFalsa(Hoje.AddDays(-1)));
        var configuracao = NovaConfiguracao(janela: 7);
        _checkpoints.Avancar(configuracao.Id, configuracao.Rotina, new Checkpoint(dia, doCheckpoint));

        var resumo = await ExecutarAsync(configuracao);

        Assert.Equal(ResultadoExecucao.Sucesso, resumo.Resultado);
        Assert.Equal([limiteDaJanela, doCheckpoint, nova], _cliente.VendasEnviadas);
        Assert.Equal(new Checkpoint(Hoje.AddDays(-1), nova), CheckpointDe(configuracao));
    }

    [Fact]
    public async Task Periodo_sem_vendas_termina_com_Sucesso_0_registros_e_avanca_o_ultimo_sucesso()
    {
        var configuracao = NovaConfiguracao();

        var resumo = await ExecutarAsync(configuracao);

        Assert.Equal((ResultadoExecucao.Sucesso, 0, 0), (resumo.Resultado, resumo.Lidos, resumo.Enviados));
        Assert.Empty(_cliente.Lotes);
        var estado = _registro.Obter(configuracao.Id);
        Assert.Equal((resumo.Fim, ResultadoExecucao.Sucesso), (estado!.UltimoSucessoEm, estado.UltimoResultado));
        var relatorio = Assert.Single(_cliente.Relatorios);
        Assert.Equal(("Sucesso", 0, configuracao.Id, configuracao.Tenant), (relatorio.Situacao, relatorio.RegistrosEnviados,
            relatorio.ConfiguracaoId, relatorio.Tenant));
        Assert.Null(CheckpointDe(configuracao));
    }

    [Fact]
    public async Task Erro_de_consulta_termina_em_Falha_classificada_e_mantem_o_checkpoint()
    {
        var dia = Hoje.AddDays(-3);
        var nota = await _erp.InserirAsync(new NotaFalsa(dia));
        var configuracao = NovaConfiguracao();
        var anterior = new Checkpoint(dia, nota);
        _checkpoints.Avancar(configuracao.Id, configuracao.Rotina, anterior);
        _timeoutErp = TimeSpan.FromSeconds(1);
        await using var bloqueio = new SqlConnection(_erp.ConnectionString);
        await bloqueio.OpenAsync();
        await using var transacao = (SqlTransaction)await bloqueio.BeginTransactionAsync();
        await bloqueio.ExecuteAsync("UPDATE dbo.NotaSaida SET totnota = totnota + 1;", transaction: transacao);

        var resumo = await ExecutarAsync(configuracao);
        await transacao.RollbackAsync();

        Assert.Equal((ResultadoExecucao.Falha, TipoErro.Consulta), (resumo.Resultado, resumo.TipoErro));
        Assert.Equal(anterior, CheckpointDe(configuracao));
        Assert.Empty(_cliente.Lotes);
        var erro = Assert.Single(new LeitorLogs(_pasta.Arquivo("logs")).Ler(new FiltroLogs(DateOnly.FromDateTime(Hoje), DateOnly.FromDateTime(Hoje))),
            e => e.Evento == LogExecucoes.EventoErro);
        Assert.Equal((TipoErro.Consulta, resumo.ExecucaoId), (erro.TipoErro, erro.ExecucaoId));
        Assert.Equal("Falha", Assert.Single(_cliente.Relatorios).Situacao);
        Assert.Null(_registro.Obter(configuracao.Id)!.UltimoSucessoEm);
    }

    [Fact]
    public async Task Fim_da_execucao_vai_para_o_log_com_inicio_fim_duracao_e_contagens()
    {
        var dia = Hoje.AddDays(-2);
        var numeros = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            numeros.Add(await _erp.InserirAsync(new NotaFalsa(dia)));
        }

        var configuracao = NovaConfiguracao();
        var inicio = _relogio.GetLocalNow();
        _cliente.Responder = (lote, _) =>
        {
            _relogio.Advance(TimeSpan.FromSeconds(3));
            return ClienteApiFalso.Ack(lote, new VendaRejeitada(numeros[0], 1, "motivo"), new VendaRejeitada(numeros[1], 1, "motivo"));
        };

        await ExecutarAsync(configuracao);

        var fim = Assert.Single(new LeitorLogs(_pasta.Arquivo("logs")).Ler(new FiltroLogs(DateOnly.FromDateTime(Hoje), DateOnly.FromDateTime(Hoje))),
            e => e.Evento == LogExecucoes.EventoFim);
        Assert.Equal((7, 7, 2, ResultadoExecucao.Sucesso), (fim.Lidos, fim.Enviados, fim.Rejeitados, fim.Resultado));
        Assert.Equal((inicio, inicio.AddSeconds(3), TimeSpan.FromSeconds(3)), (fim.Inicio, fim.Fim, fim.Duracao));
        Assert.Equal((configuracao.Tenant, "Cliente A", "SRV-ERP,1433", _erp.NomeBanco, "Vendas"),
            (fim.Tenant, fim.NomeTenant, fim.Instancia, fim.Banco, fim.Rotina));
    }

    [Fact]
    public async Task Pedido_de_parada_encerra_depois_do_lote_confirmado_como_Cancelada()
    {
        await _erp.SemearAsync(1200, Hoje.AddDays(-170), dias: 160);
        var notas = await NotasEmOrdemAsync();
        var configuracao = NovaConfiguracao();

        var resumo = await ExecutarAsync(configuracao, parar: () => _cliente.Lotes.Count >= 1);

        Assert.Equal((ResultadoExecucao.Cancelada, 500), (resumo.Resultado, resumo.Enviados));
        Assert.Single(_cliente.Lotes);
        Assert.Equal(notas[499], CheckpointDe(configuracao));
    }
}
