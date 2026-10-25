using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Contratos.Coletor;
using Jamsoft.DashBoards.Contratos.Ingestao;

namespace Jamsoft.DashBoards.Coletor;

/// <summary>
/// O <see cref="IClienteApi"/> do executor, que só existe depois do login: delega ao <see cref="ClienteApi"/> da
/// credencial atual, trocado a cada login. Guarda também se a tela está autenticada (CFG-01) e os tenants
/// autorizados (CFG-02). Sem credencial, toda chamada volta como <see cref="SituacaoResposta.CredencialInvalida"/>,
/// e o agendador para até o login.
/// </summary>
internal sealed class SessaoApi(TimeProvider relogio) : IClienteApi
{
    private readonly Lock _lock = new();
    private ClienteApi? _cliente;

    public bool Autenticada { get; private set; }

    public IReadOnlyList<TenantAutorizado> Tenants { get; private set; } = [];

    /// <summary>Login feito ou perdido (na thread de quem chamou).</summary>
    public event EventHandler? Alterada;

    /// <summary>
    /// Usa a credencial gravada sem autenticar: depois de um reinício o agendador retoma (SCH-05) mesmo com a API fora;
    /// a tela só libera as alterações depois de um <see cref="EntrarAsync"/> com sucesso.
    /// </summary>
    public void Usar(CredencialApi credencial)
    {
        lock (_lock)
        {
            _cliente = new ClienteApi(credencial, relogio);
        }
    }

    /// <summary>Autentica com a credencial e, no sucesso, passa a usá-la e guarda os tenants autorizados (CFG-02).</summary>
    public async Task<RespostaApi<IReadOnlyList<TenantAutorizado>>> EntrarAsync(CredencialApi credencial, CancellationToken ct = default)
    {
        var candidato = new ClienteApi(credencial, relogio);
        var resposta = await candidato.AutenticarAsync(ct);
        if (!resposta.Sucesso)
        {
            candidato.Dispose();
            return resposta;
        }

        lock (_lock)
        {
            // O cliente anterior não é descartado: pode haver um lote em andamento com ele.
            _cliente = candidato;
        }

        Tenants = resposta.Valor!;
        Autenticada = true;
        Alterada?.Invoke(this, EventArgs.Empty);
        return resposta;
    }

    /// <summary>Tenants autorizados atualizados pela API; na falha, os do último login.</summary>
    public async Task<IReadOnlyList<TenantAutorizado>> AtualizarTenantsAsync(CancellationToken ct = default)
    {
        if (Atual() is { } cliente && Autenticada)
        {
            var resposta = await cliente.ListarTenantsAsync(ct);
            if (resposta.Sucesso)
            {
                Tenants = resposta.Valor!;
            }
        }

        return Tenants;
    }

    /// <summary>A API recusou a credencial: a tela volta a exigir login (CFG-01).</summary>
    public void Encerrar()
    {
        Autenticada = false;
        Alterada?.Invoke(this, EventArgs.Empty);
    }

    public Task<RespostaApi<ResultadoLote>> EnviarLoteAsync(LoteVendas lote, CancellationToken ct = default) =>
        Atual() is { } cliente ? cliente.EnviarLoteAsync(lote, ct) : Task.FromResult(SemLogin<ResultadoLote>());

    public Task<RespostaApi<bool>> ReportarExecucaoAsync(RelatorioExecucao relatorio, CancellationToken ct = default) =>
        Atual() is { } cliente ? cliente.ReportarExecucaoAsync(relatorio, ct) : Task.FromResult(SemLogin<bool>());

    private ClienteApi? Atual()
    {
        lock (_lock)
        {
            return _cliente;
        }
    }

    private static RespostaApi<T> SemLogin<T>() => new(SituacaoResposta.CredencialInvalida, Mensagem: "Coletor sem login na API.");
}
