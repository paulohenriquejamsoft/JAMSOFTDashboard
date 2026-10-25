using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Contratos.Ingestao;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

/// <summary>
/// Como terminou uma execução (LOG-02). <see cref="Recusa"/> traz <see cref="SituacaoResposta.NaoAutorizada"/> ou
/// <see cref="SituacaoResposta.CredencialInvalida"/> quando a API recusou: o agendador para a configuração ou tudo.
/// </summary>
public sealed record ResumoExecucao(
    Guid ExecucaoId,
    ResultadoExecucao Resultado,
    DateTimeOffset Inicio,
    DateTimeOffset Fim,
    int Lidos,
    int Enviados,
    int Rejeitados,
    TipoErro? TipoErro = null,
    string? Mensagem = null,
    SituacaoResposta? Recusa = null);

/// <summary>Executa uma configuração uma vez (permite um executor falso nos testes do agendador).</summary>
public interface IExecutorSincronizacao
{
    /// <param name="pararEntreLotes">Consultado depois de cada lote confirmado: verdadeiro encerra como Cancelada (CTL-01, CTL-06).</param>
    Task<ResumoExecucao> ExecutarAsync(ConfiguracaoSincronizacao configuracao, Func<bool> pararEntreLotes, CancellationToken ct);
}

/// <summary>
/// Uma execução de uma configuração (SYNC-01..06, SYNC-08): define a janela (carga inicial desde o período inicial,
/// padrão hoje − 6 meses; incremental desde o checkpoint − janela de reprocessamento) até a hora do servidor do ERP,
/// lê lotes de 500, envia, grava o checkpoint só depois do ACK, registra o fim no log e no
/// <see cref="RegistroExecucoes"/> e reporta à API.
/// </summary>
public sealed class ExecutorSincronizacao(
    ArmazenamentoConfiguracao armazenamento,
    Checkpoints checkpoints,
    RegistroExecucoes registro,
    LogExecucoes log,
    IClienteApi cliente,
    Func<InstanciaConexao, string, ExtratorVendas> extratores,
    TimeProvider relogio) : IExecutorSincronizacao
{
    public const string EventoInicio = "execucao_inicio";
    public const string EventoAviso = "aviso";

    public async Task<ResumoExecucao> ExecutarAsync(ConfiguracaoSincronizacao configuracao, Func<bool> pararEntreLotes, CancellationToken ct)
    {
        var inicio = relogio.GetLocalNow();
        var instancia = armazenamento.Carregar().Instancias.FirstOrDefault(i => i.Id == configuracao.InstanciaId);
        var nomeInstancia = instancia is null ? "?" : instancia.Porta is { } porta ? $"{instancia.Servidor},{porta}" : instancia.Servidor;
        var contexto = new ContextoExecucao(Guid.NewGuid(), configuracao.Tenant, configuracao.NomeTenant, nomeInstancia,
            configuracao.Banco, configuracao.Rotina);
        var bancoOrigem = $"{nomeInstancia}/{configuracao.Banco}";
        log.Registrar(contexto, NivelLog.Informacao, EventoInicio, "Início da execução.");

        int lidos = 0, enviados = 0, rejeitados = 0;
        var resultado = ResultadoExecucao.Sucesso;
        TipoErro? tipoErro = null;
        string? mensagem = null;
        SituacaoResposta? recusa = null;
        try
        {
            if (instancia is null)
            {
                throw new InvalidOperationException("A instância de conexão da configuração não existe mais.");
            }

            var extrator = extratores(instancia, configuracao.Banco);
            // Limite superior pela hora do servidor do ERP, não do Coletor (edge case do relógio, SYNC-01).
            var fim = (await extrator.AgoraDoServidorAsync(ct)).DateTime;
            var checkpoint = checkpoints.Obter(configuracao.Id, configuracao.Rotina);
            var desde = checkpoint is null
                ? (configuracao.PeriodoInicial ?? armazenamento.PeriodoInicialPadrao()).ToDateTime(TimeOnly.MinValue)
                : checkpoint.DataFatura.Date.AddDays(-configuracao.JanelaReprocessamentoDias);

            Checkpoint? cursor = null;
            while (true)
            {
                if (pararEntreLotes())
                {
                    resultado = ResultadoExecucao.Cancelada;
                    mensagem = "Interrompida entre lotes a pedido do técnico.";
                    break;
                }

                var lote = await extrator.LerLoteAsync(desde, fim, cursor, Limites.VendasPorLote, ct);
                if (lote.Count == 0)
                {
                    break;
                }

                lidos += lote.Count;
                foreach (var semFechamento in lote.Where(v => v.SemDataHoraFechamento))
                {
                    log.Registrar(contexto, NivelLog.Aviso, EventoAviso,
                        $"Venda {semFechamento.VendaId} sem DataHoraFechamento: enviada com a datafatura às 00:00.");
                }

                var resposta = await cliente.EnviarLoteAsync(new LoteVendas(Guid.NewGuid(), configuracao.Tenant, configuracao.Id,
                    configuracao.Rotina, bancoOrigem, [.. lote.Select(v => v.ParaContrato())]), ct);
                if (!resposta.Sucesso)
                {
                    resultado = ResultadoExecucao.Falha;
                    tipoErro = resposta.Situacao == SituacaoResposta.FalhaComunicacao ? TipoErro.Comunicacao : TipoErro.Processamento;
                    mensagem = resposta.Codigo is null ? resposta.Mensagem : $"{resposta.Codigo}: {resposta.Mensagem}";
                    recusa = resposta.Situacao is SituacaoResposta.NaoAutorizada or SituacaoResposta.CredencialInvalida ? resposta.Situacao : null;
                    break;
                }

                // ACK: só agora o checkpoint avança (SYNC-03); nunca volta para trás numa execução incremental.
                enviados += lote.Count;
                rejeitados += resposta.Valor!.Rejeitados.Count;
                cursor = lote[^1].Posicao;
                if (checkpoint is null || Depois(cursor, checkpoint))
                {
                    checkpoints.Avancar(configuracao.Id, configuracao.Rotina, cursor);
                    checkpoint = cursor;
                }

                if (lote.Count < Limites.VendasPorLote)
                {
                    break;
                }
            }
        }
        catch (ErroErpException erro)
        {
            (resultado, tipoErro, mensagem) = (ResultadoExecucao.Falha, erro.Tipo, erro.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            (resultado, mensagem) = (ResultadoExecucao.Cancelada, "Coletor encerrado durante a execução.");
        }
        catch (Exception erro)
        {
            (resultado, tipoErro, mensagem) = (ResultadoExecucao.Falha, TipoErro.Processamento, erro.Message);
        }

        if (tipoErro is { } tipo)
        {
            log.RegistrarErro(contexto, tipo, mensagem ?? "");
        }

        log.RegistrarFim(contexto, inicio, lidos, enviados, rejeitados, resultado);
        var termino = relogio.GetLocalNow();
        registro.Registrar(configuracao.Id, resultado, termino, mensagem, recusa == SituacaoResposta.NaoAutorizada);

        // Com o tenant ou a credencial recusados a API recusaria o relatório também; encerrando, não há a quem reportar.
        if (recusa is null && !ct.IsCancellationRequested)
        {
            var relatorio = await cliente.ReportarExecucaoAsync(new RelatorioExecucao(configuracao.Tenant, configuracao.Id,
                configuracao.Rotina, bancoOrigem, inicio, termino, resultado.ToString(), enviados, mensagem), ct);
            if (!relatorio.Sucesso)
            {
                log.Registrar(contexto, NivelLog.Aviso, EventoAviso, $"Relatório da execução não chegou à API: {relatorio.Mensagem}");
            }
        }

        return new ResumoExecucao(contexto.ExecucaoId, resultado, inicio, termino, lidos, enviados, rejeitados, tipoErro, mensagem, recusa);
    }

    private static bool Depois(Checkpoint a, Checkpoint b) =>
        a.DataFatura > b.DataFatura || (a.DataFatura == b.DataFatura && a.NumNotaSaida > b.NumNotaSaida);
}
