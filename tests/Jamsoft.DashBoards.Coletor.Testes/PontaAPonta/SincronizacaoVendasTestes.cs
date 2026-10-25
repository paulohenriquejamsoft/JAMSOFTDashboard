using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Coletor.Testes.PontaAPonta;

/// <summary>
/// O Independent Test do spec com o Nucleo real, o ERP falso e a API real em memória: carga inicial, incremental com
/// vendas novas e cancelamento dentro da janela, reinício sem duplicar e queda da API no meio da carga
/// (SYNC-05, SCH-05, CTL-05, ING-04, ING-05).
/// </summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class SincronizacaoVendasTestes(BancoDeTestes banco) : IAsyncLifetime, IDisposable
{
    // Retry curto: a queda da API é simulada por tentativa, sem esperar os 2..32 s de produção.
    private static readonly TimeSpan[] RetryCurto = [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(5), 5)];

    private readonly PastaTemporaria _pasta = new();
    private readonly InstanciaConexao _instancia = new(Guid.NewGuid(), "SRV-ERP", null, TipoAutenticacao.Windows, null, null);
    private ApiEmMemoria _api = null!;
    private ErpFalso _erp = null!;
    private Tenant _tenant = null!;
    private TransporteGravado _transporte = null!;
    private ClienteApi _cliente = null!;
    private Checkpoints _checkpoints = null!;
    private ArmazenamentoConfiguracao _armazenamento = null!;
    private ExecutorSincronizacao _executor = null!;
    private ConfiguracaoSincronizacao _configuracao = null!;

    private static DateTime Hoje => DateTime.Today;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiEmMemoria(banco);
        _erp = await ErpFalso.CriarAsync(banco);
        _tenant = await DadosPlataforma.NovoTenantAsync(banco, "Cliente ponta a ponta");
        var (_, credencial) = await DadosPlataforma.NovoColetorAsync(banco, _tenant);
        _transporte = new TransporteGravado(_api.Server.CreateHandler(), TimeProvider.System);
        _cliente = new ClienteApi(credencial, TimeProvider.System, _transporte, RetryCurto);

        _checkpoints = new Checkpoints(_pasta.Caminho);
        _armazenamento = new ArmazenamentoConfiguracao(_pasta.Caminho, _checkpoints, TimeProvider.System);
        _armazenamento.SalvarInstancia(_instancia);
        _configuracao = new ConfiguracaoSincronizacao(Guid.NewGuid(), _tenant.IdPublico, _tenant.Nome, _instancia.Id, _erp.NomeBanco,
            ConfiguracaoSincronizacao.RotinaVendas, ConfiguracaoSincronizacao.IntervaloPadrao, null,
            ConfiguracaoSincronizacao.JanelaPadraoDias, SituacaoConfiguracao.Ativa);
        _armazenamento.SalvarConfiguracao(_configuracao);
        _executor = new ExecutorSincronizacao(_armazenamento, _checkpoints, new RegistroExecucoes(_pasta.Caminho),
            new LogExecucoes(_pasta.Arquivo("logs"), TimeProvider.System), _cliente, (_, _) => _erp.Extrator(), TimeProvider.System);
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    public void Dispose() => _pasta.Dispose();

    private Task<ResumoExecucao> ExecutarAsync() => _executor.ExecutarAsync(_configuracao, () => false, CancellationToken.None);

    private Task<int> VendasNaPlataformaAsync() => DadosPlataforma.VendasAsync(banco, _tenant);

    private async Task<(int Total, int Distintas)> ContagemAsync()
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(_tenant.Id));
        return (await contexto.Vendas.CountAsync(), await contexto.Vendas.Select(v => new { v.CodEmpresa, v.VendaId }).Distinct().CountAsync());
    }

    /// <summary>1.200 vendas finalizadas nos últimos 6 meses; a mais recente fica 6 dias atrás.</summary>
    private Task SemearSeisMesesAsync(int quantidade = 1200) => _erp.SemearAsync(quantidade, Hoje.AddDays(-170), dias: 165);

    [Fact]
    public async Task Carga_inicial_de_1200_vendas_em_6_meses_chega_inteira_em_3_lotes()
    {
        await SemearSeisMesesAsync();

        var resumo = await ExecutarAsync();

        Assert.Equal((ResultadoExecucao.Sucesso, 1200, 1200), (resumo.Resultado, resumo.Lidos, resumo.Enviados));
        Assert.Equal(3, _transporte.Da(ClienteApi.RotaLotes).Count);
        Assert.Equal((1200, 1200), await ContagemAsync());
    }

    [Fact]
    public async Task Incremental_traz_10_novas_e_o_cancelamento_na_janela()
    {
        await SemearSeisMesesAsync();
        Assert.Equal(ResultadoExecucao.Sucesso, (await ExecutarAsync()).Resultado);
        var cancelada = await _erp.ConsultarAsync<int>("SELECT MAX(NumNotaSaida) FROM dbo.NotaSaida WHERE datafatura = @Dia;",
            new { Dia = Hoje.AddDays(-8) });
        await _erp.ExecutarAsync("UPDATE dbo.NotaSaida SET situacaonota = 9 WHERE NumNotaSaida = @Num;", new { Num = cancelada });
        for (var i = 0; i < 10; i++)
        {
            await _erp.InserirAsync(new NotaFalsa(Hoje.AddDays(-1)));
        }

        var incremental = await ExecutarAsync();

        Assert.Equal(ResultadoExecucao.Sucesso, incremental.Resultado);
        Assert.Equal((1210, 1210), await ContagemAsync());
        await using var contexto = banco.CriarContexto(new TenantFixo(_tenant.Id));
        Assert.Equal("Cancelada", await contexto.Vendas.Where(v => v.VendaId == cancelada).Select(v => v.Situacao).SingleAsync());
        Assert.Equal(1209, await contexto.Vendas.CountAsync(v => v.Situacao == "Finalizada"));
    }

    [Fact]
    public async Task Reiniciar_pelo_agendador_recarrega_tudo_sem_duplicar()
    {
        await SemearSeisMesesAsync(1210);
        var comandos = new ComandosConfiguracao();
        var agendador = new Agendador(_armazenamento, _checkpoints, _executor, comandos, TimeProvider.System);
        await agendador.StartAsync(CancellationToken.None);
        try
        {
            await Esperar.AteAsync(() => agendador.Estado(_configuracao.Id)?.UltimoResumo is { Resultado: ResultadoExecucao.Sucesso },
                "carga inicial", 60_000);
            var carga = agendador.Estado(_configuracao.Id)!.UltimoResumo!;
            Assert.Equal((1210, 1210), await ContagemAsync());

            comandos.Reiniciar(_configuracao.Id);
            await Esperar.AteAsync(() => agendador.Estado(_configuracao.Id)?.UltimoResumo is { } r && r.ExecucaoId != carga.ExecucaoId,
                "nova carga depois do reinício", 60_000);

            var recarga = agendador.Estado(_configuracao.Id)!.UltimoResumo!;
            Assert.Equal((ResultadoExecucao.Sucesso, 1210), (recarga.Resultado, recarga.Enviados));
            Assert.Equal((1210, 1210), await ContagemAsync());
        }
        finally
        {
            await agendador.StopAsync(CancellationToken.None);
            agendador.Dispose();
        }
    }

    [Fact]
    public async Task API_derrubada_duas_vezes_durante_a_carga_de_3000_termina_com_a_contagem_exata()
    {
        await SemearSeisMesesAsync(3000);
        // Duas quedas longas o bastante para esgotar o retry (1 + 5 tentativas cada): no 2º e no 4º lote enviados.
        var quedas = new HashSet<int>([.. Enumerable.Range(2, 6), .. Enumerable.Range(10, 6)]);
        _transporte.Interceptar = (requisicao, numero) =>
            requisicao.RequestUri!.AbsolutePath.EndsWith(ClienteApi.RotaLotes, StringComparison.Ordinal) && quedas.Contains(numero)
                ? throw new HttpRequestException("API fora do ar")
                : null;

        var resultados = new List<ResumoExecucao>();
        do
        {
            resultados.Add(await ExecutarAsync());
        }
        while (resultados[^1].Resultado != ResultadoExecucao.Sucesso && resultados.Count < 5);

        Assert.Equal([ResultadoExecucao.Falha, ResultadoExecucao.Falha, ResultadoExecucao.Sucesso], resultados.Select(r => r.Resultado));
        Assert.All(resultados.Take(2), r => Assert.Equal(TipoErro.Comunicacao, r.TipoErro));
        Assert.Equal((3000, 3000), await ContagemAsync());
    }
}
