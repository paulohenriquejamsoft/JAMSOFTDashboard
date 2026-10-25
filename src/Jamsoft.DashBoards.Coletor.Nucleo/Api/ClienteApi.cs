using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Contratos.Coletor;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Api;

/// <summary>Como terminou uma chamada à API, já traduzida para o Coletor.</summary>
public enum SituacaoResposta
{
    Sucesso,

    /// <summary>401 mesmo depois de renovar o token: credencial revogada ou rotacionada; tudo para até novo login.</summary>
    CredencialInvalida,

    /// <summary>403 <c>TENANT_NAO_AUTORIZADO</c> ou <c>TENANT_INATIVO</c>: a configuração vira "Não autorizada".</summary>
    NaoAutorizada,

    /// <summary>Outra recusa da API (400, 409, 413...): não adianta repetir.</summary>
    Recusada,

    /// <summary>Rede, timeout ou 5xx depois de esgotar as tentativas.</summary>
    FalhaComunicacao,
}

/// <summary>Resultado tipado de uma chamada: o valor no sucesso; status HTTP, <c>codigo</c> e mensagem na falha.</summary>
public sealed record RespostaApi<T>(SituacaoResposta Situacao, T? Valor = default, int? Status = null, string? Codigo = null,
    string? Mensagem = null)
{
    public bool Sucesso => Situacao == SituacaoResposta.Sucesso;

    internal RespostaApi<TOutro> Como<TOutro>() => new(Situacao, default, Status, Codigo, Mensagem);
}

/// <summary>O que o executor precisa da API (permite um cliente falso nos testes).</summary>
public interface IClienteApi
{
    /// <summary><c>POST /api/coletor/vendas/lotes</c>: ACK do lote (SYNC-03).</summary>
    Task<RespostaApi<ResultadoLote>> EnviarLoteAsync(LoteVendas lote, CancellationToken ct = default);

    /// <summary><c>POST /api/coletor/execucoes</c> (ING-09).</summary>
    Task<RespostaApi<bool>> ReportarExecucaoAsync(RelatorioExecucao relatorio, CancellationToken ct = default);
}

/// <summary>
/// Cliente HTTP do Coletor (COL-03, SYNC-04). O token é guardado até 1 min antes de expirar; um 401 renova uma vez e
/// reenvia a mesma requisição. Falhas transitórias (rede, timeout, 408, 429, 5xx) são repetidas com os
/// <see cref="IntervalosPadrao"/> (2, 4, 8, 16 e 32 s); 4xx nunca é repetido.
/// </summary>
public sealed class ClienteApi : IClienteApi, IDisposable
{
    public const string RotaToken = "api/coletor/token";
    public const string RotaTenants = "api/coletor/tenants";
    public const string RotaLotes = "api/coletor/vendas/lotes";
    public const string RotaExecucoes = "api/coletor/execucoes";

    public static readonly IReadOnlyList<TimeSpan> IntervalosPadrao =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(32)];

    /// <summary>Tempo limite de cada tentativa; esgotado, conta como falha transitória.</summary>
    public static readonly TimeSpan TimeoutPorTentativa = TimeSpan.FromSeconds(100);

    public static readonly TimeSpan MargemRenovacao = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CredencialApi _credencial;
    private readonly TimeProvider _relogio;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _lockToken = new(1, 1);
    private TokenColetorResposta? _token;

    /// <param name="transporte">Handler de rede; padrão <see cref="SocketsHttpHandler"/> (os testes passam o da API em memória).</param>
    /// <param name="intervalosRetry">Espera antes de cada nova tentativa; padrão <see cref="IntervalosPadrao"/>.</param>
    public ClienteApi(CredencialApi credencial, TimeProvider relogio, HttpMessageHandler? transporte = null,
        IReadOnlyList<TimeSpan>? intervalosRetry = null)
    {
        _credencial = credencial;
        _relogio = relogio;
        var intervalos = intervalosRetry ?? IntervalosPadrao;
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = relogio }
            .AddRetry(new HttpRetryStrategyOptions
            {
                // Antes do DelayGenerator: o setter de ShouldRetryAfterHeader troca o gerador de espera.
                ShouldRetryAfterHeader = false,
                MaxRetryAttempts = intervalos.Count,
                DelayGenerator = argumentos => ValueTask.FromResult<TimeSpan?>(intervalos[argumentos.AttemptNumber]),
            })
            .AddTimeout(TimeoutPorTentativa)
            .Build();
        _http = new HttpClient(new ResilienceHandler(pipeline) { InnerHandler = transporte ?? new SocketsHttpHandler() })
        {
            BaseAddress = new Uri(credencial.UrlApi.TrimEnd('/') + "/"),
            // Cada tentativa tem o próprio limite; o total fica por conta do retry.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>Pede um token novo e devolve os tenants autorizados e ativos (CFG-02).</summary>
    public async Task<RespostaApi<IReadOnlyList<TenantAutorizado>>> AutenticarAsync(CancellationToken ct = default)
    {
        var token = await TokenAsync(renovarSeFor: _token, ct);
        return token.Sucesso
            ? new RespostaApi<IReadOnlyList<TenantAutorizado>>(SituacaoResposta.Sucesso, token.Valor!.Tenants)
            : token.Como<IReadOnlyList<TenantAutorizado>>();
    }

    /// <summary><c>GET /api/coletor/tenants</c> (CFG-05).</summary>
    public Task<RespostaApi<IReadOnlyList<TenantAutorizado>>> ListarTenantsAsync(CancellationToken ct = default) =>
        EnviarAutenticadoAsync(() => new HttpRequestMessage(HttpMethod.Get, RotaTenants),
            async resposta => (IReadOnlyList<TenantAutorizado>)(await resposta.Content.ReadFromJsonAsync<List<TenantAutorizado>>(Json, ct))!, ct);

    public Task<RespostaApi<ResultadoLote>> EnviarLoteAsync(LoteVendas lote, CancellationToken ct = default) =>
        EnviarAutenticadoAsync(() => new HttpRequestMessage(HttpMethod.Post, RotaLotes) { Content = JsonContent.Create(lote, options: Json) },
            async resposta => (await resposta.Content.ReadFromJsonAsync<ResultadoLote>(Json, ct))!, ct);

    public Task<RespostaApi<bool>> ReportarExecucaoAsync(RelatorioExecucao relatorio, CancellationToken ct = default) =>
        EnviarAutenticadoAsync(() => new HttpRequestMessage(HttpMethod.Post, RotaExecucoes) { Content = JsonContent.Create(relatorio, options: Json) },
            _ => Task.FromResult(true), ct);

    public void Dispose()
    {
        _http.Dispose();
        _lockToken.Dispose();
    }

    private async Task<RespostaApi<T>> EnviarAutenticadoAsync<T>(Func<HttpRequestMessage> criar,
        Func<HttpResponseMessage, Task<T>> ler, CancellationToken ct)
    {
        TokenColetorResposta? recusado = null;
        while (true)
        {
            var token = await TokenAsync(recusado, ct);
            if (!token.Sucesso)
            {
                return token.Como<T>();
            }

            using var requisicao = criar();
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Valor!.AccessToken);
            var (resposta, falha) = await EnviarAsync(requisicao, ct);
            if (falha is not null)
            {
                return falha.Como<T>();
            }

            using (resposta)
            {
                if (resposta!.StatusCode == HttpStatusCode.Unauthorized && recusado is null)
                {
                    // Token expirado ou de uma versão antiga da credencial: renova uma vez e reenvia.
                    recusado = token.Valor;
                    continue;
                }

                if (resposta.IsSuccessStatusCode)
                {
                    return new RespostaApi<T>(SituacaoResposta.Sucesso, await ler(resposta), (int)resposta.StatusCode);
                }

                return await TraduzirFalhaAsync<T>(resposta, ct);
            }
        }
    }

    /// <summary>Token em cache ou um novo; <paramref name="renovarSeFor"/> descarta esse token (recusado pela API).</summary>
    private async Task<RespostaApi<TokenColetorResposta>> TokenAsync(TokenColetorResposta? renovarSeFor, CancellationToken ct)
    {
        await _lockToken.WaitAsync(ct);
        try
        {
            if (_token is { } atual && !ReferenceEquals(atual, renovarSeFor) && atual.ExpiraEm - MargemRenovacao > _relogio.GetUtcNow())
            {
                return new RespostaApi<TokenColetorResposta>(SituacaoResposta.Sucesso, atual);
            }

            _token = null;
            using var requisicao = new HttpRequestMessage(HttpMethod.Post, RotaToken)
            {
                Content = JsonContent.Create(new TokenColetorRequisicao(_credencial.ClientId, _credencial.ClientSecret), options: Json),
            };
            var (resposta, falha) = await EnviarAsync(requisicao, ct);
            if (falha is not null)
            {
                return falha.Como<TokenColetorResposta>();
            }

            using (resposta)
            {
                if (resposta!.IsSuccessStatusCode)
                {
                    _token = await resposta.Content.ReadFromJsonAsync<TokenColetorResposta>(Json, ct);
                    return new RespostaApi<TokenColetorResposta>(SituacaoResposta.Sucesso, _token);
                }

                return await TraduzirFalhaAsync<TokenColetorResposta>(resposta, ct);
            }
        }
        finally
        {
            _lockToken.Release();
        }
    }

    private async Task<(HttpResponseMessage? Resposta, RespostaApi<bool>? Falha)> EnviarAsync(HttpRequestMessage requisicao, CancellationToken ct)
    {
        try
        {
            return (await _http.SendAsync(requisicao, ct), null);
        }
        catch (Exception erro) when (erro is HttpRequestException or TimeoutRejectedException && !ct.IsCancellationRequested)
        {
            return (null, new RespostaApi<bool>(SituacaoResposta.FalhaComunicacao, Mensagem: $"API indisponível: {erro.Message}"));
        }
    }

    private static async Task<RespostaApi<T>> TraduzirFalhaAsync<T>(HttpResponseMessage resposta, CancellationToken ct)
    {
        var (codigo, titulo) = await ProblemaAsync(resposta, ct);
        var status = (int)resposta.StatusCode;
        var situacao = resposta.StatusCode switch
        {
            HttpStatusCode.Unauthorized => SituacaoResposta.CredencialInvalida,
            HttpStatusCode.Forbidden when codigo is "TENANT_NAO_AUTORIZADO" or "TENANT_INATIVO" => SituacaoResposta.NaoAutorizada,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => SituacaoResposta.FalhaComunicacao,
            _ when status >= 500 => SituacaoResposta.FalhaComunicacao,
            _ => SituacaoResposta.Recusada,
        };
        return new RespostaApi<T>(situacao, default, status, codigo, titulo ?? $"HTTP {status}");
    }

    /// <summary><c>codigo</c> e <c>title</c> do ProblemDetails (AD-011), quando houver.</summary>
    private static async Task<(string? Codigo, string? Titulo)> ProblemaAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
            return (json.RootElement.TryGetProperty("codigo", out var c) ? c.GetString() : null,
                json.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
