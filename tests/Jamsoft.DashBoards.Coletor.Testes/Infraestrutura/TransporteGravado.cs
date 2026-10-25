using System.Net;

namespace Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

/// <summary>Uma requisição que chegou ao transporte: rota, status devolvido e hora no relógio do teste.</summary>
public sealed record ChamadaGravada(string Rota, HttpStatusCode Status, DateTimeOffset Em);

/// <summary>
/// Transporte entre o <c>ClienteApi</c> e a API em memória: grava cada chamada e, quando <see cref="Interceptar"/>
/// devolve uma resposta, ela substitui a da API (simula a API fora do ar).
/// </summary>
public sealed class TransporteGravado(HttpMessageHandler api, TimeProvider relogio) : DelegatingHandler(api)
{
    private readonly List<ChamadaGravada> _chamadas = [];

    /// <summary>Recebe a requisição e o número dela na rota (1, 2, ...); nulo deixa passar para a API.</summary>
    public Func<HttpRequestMessage, int, HttpResponseMessage?>? Interceptar { get; set; }

    public IReadOnlyList<ChamadaGravada> Chamadas
    {
        get
        {
            lock (_chamadas)
            {
                return [.. _chamadas];
            }
        }
    }

    public IReadOnlyList<ChamadaGravada> Da(string rota) => [.. Chamadas.Where(c => c.Rota == rota)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var rota = request.RequestUri!.AbsolutePath.TrimStart('/');
        var agora = relogio.GetUtcNow();
        int numero;
        lock (_chamadas)
        {
            numero = _chamadas.Count(c => c.Rota == rota) + 1;
        }

        var resposta = Interceptar?.Invoke(request, numero) ?? await base.SendAsync(request, cancellationToken);
        lock (_chamadas)
        {
            _chamadas.Add(new ChamadaGravada(rota, resposta.StatusCode, agora));
        }

        return resposta;
    }
}
