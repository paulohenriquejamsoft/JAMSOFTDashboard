using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Contratos.Ingestao;

namespace Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

/// <summary>
/// <see cref="IClienteApi"/> controlável: grava cada lote recebido com o checkpoint que existia no momento do envio e
/// responde com <see cref="Responder"/> (padrão: ACK com todas inseridas).
/// </summary>
public sealed class ClienteApiFalso(Checkpoints? checkpoints = null) : IClienteApi
{
    private readonly List<(LoteVendas Lote, Checkpoint? CheckpointNoEnvio)> _lotes = [];
    private readonly List<RelatorioExecucao> _relatorios = [];

    /// <summary>Recebe o lote e o número dele (1, 2, ...).</summary>
    public Func<LoteVendas, int, RespostaApi<ResultadoLote>>? Responder { get; set; }

    public IReadOnlyList<(LoteVendas Lote, Checkpoint? CheckpointNoEnvio)> Lotes => [.. _lotes];

    public IReadOnlyList<RelatorioExecucao> Relatorios => [.. _relatorios];

    public IReadOnlyList<int> VendasEnviadas => [.. _lotes.SelectMany(l => l.Lote.Vendas.Select(v => v.VendaId))];

    public static RespostaApi<ResultadoLote> Ack(LoteVendas lote, params VendaRejeitada[] rejeitadas) =>
        new(SituacaoResposta.Sucesso, new ResultadoLote(lote.BatchId, lote.Vendas.Count - rejeitadas.Length, 0, rejeitadas), 200);

    public static RespostaApi<ResultadoLote> ApiForaDoAr() =>
        new(SituacaoResposta.FalhaComunicacao, Mensagem: "API indisponível: conexão recusada.");

    public Task<RespostaApi<ResultadoLote>> EnviarLoteAsync(LoteVendas lote, CancellationToken ct = default)
    {
        _lotes.Add((lote, checkpoints?.Obter(lote.ConfiguracaoId, lote.Rotina)));
        return Task.FromResult(Responder?.Invoke(lote, _lotes.Count) ?? Ack(lote));
    }

    public Task<RespostaApi<bool>> ReportarExecucaoAsync(RelatorioExecucao relatorio, CancellationToken ct = default)
    {
        _relatorios.Add(relatorio);
        return Task.FromResult(new RespostaApi<bool>(SituacaoResposta.Sucesso, true, 204));
    }

    public void Limpar()
    {
        _lotes.Clear();
        _relatorios.Clear();
    }
}
