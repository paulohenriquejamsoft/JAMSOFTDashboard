using System.Net.Http.Json;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Seguranca;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Chamadas às rotas de <c>/api/auth</c> e leitura do cookie de refresh.</summary>
public static class Sessao
{
    public const string CabecalhoCsrf = "X-Requested-With";
    public const string ValorCsrf = "jamsoft";

    public static Task<HttpResponseMessage> LoginAsync(HttpClient cliente, string email, string senha = Dados.Senha) =>
        cliente.PostAsJsonAsync("/api/auth/login", new { email, senha });

    /// <summary>Linha Set-Cookie do cookie de refresh, ou nula.</summary>
    public static string? SetCookieRefresh(HttpResponseMessage resposta) =>
        resposta.Headers.TryGetValues("Set-Cookie", out var valores)
            ? valores.FirstOrDefault(v => v.StartsWith($"{RefreshTokens.Cookie}=", StringComparison.Ordinal))
            : null;

    /// <summary>Valor do cookie de refresh gravado pela resposta.</summary>
    public static string CookieRefresh(HttpResponseMessage resposta)
    {
        var linha = SetCookieRefresh(resposta) ?? throw new InvalidOperationException("Resposta sem cookie de refresh.");
        return Uri.UnescapeDataString(linha[(RefreshTokens.Cookie.Length + 1)..linha.IndexOf(';')]);
    }

    /// <summary>POST /api/auth/{rota} com o cookie de refresh (quando informado) e o cabeçalho anti-CSRF.</summary>
    public static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string rota, string? refresh,
        bool comCsrf = true, string? accessToken = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{rota}");
        if (refresh is not null) requisicao.Headers.Add("Cookie", $"{RefreshTokens.Cookie}={Uri.EscapeDataString(refresh)}");
        if (comCsrf) requisicao.Headers.Add(CabecalhoCsrf, ValorCsrf);
        if (accessToken is not null) requisicao.Headers.Authorization = new("Bearer", accessToken);
        return cliente.SendAsync(requisicao);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        (await JsonAsync(resposta)).GetProperty("codigo").GetString();
}
