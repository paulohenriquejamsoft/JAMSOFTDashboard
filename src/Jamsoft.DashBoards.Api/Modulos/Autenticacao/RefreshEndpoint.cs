using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Seguranca;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

/// <summary>
/// <c>POST /api/auth/refresh</c> (AUTH-04, AUTH-05): troca o refresh token do cookie por um novo par.
/// Exige <c>X-Requested-With: jamsoft</c>: um formulário de outro site não consegue enviar o cabeçalho.
/// </summary>
public static class RefreshEndpoint
{
    public const string CabecalhoCsrf = "X-Requested-With";
    public const string ValorCsrf = "jamsoft";

    public static async Task<IResult> RenovarAsync(HttpContext http, RefreshTokens refreshTokens, TokenService tokens,
        CancellationToken ct)
    {
        if (!TemCabecalhoCsrf(http.Request))
            return ProblemasApi.Problema(StatusCodes.Status400BadRequest, "REQUISICAO_INVALIDA",
                $"Cabeçalho {CabecalhoCsrf} ausente");

        var token = http.Request.Cookies[RefreshTokens.Cookie];
        var renovado = string.IsNullOrEmpty(token) ? null : await refreshTokens.RenovarAsync(token, ct);
        if (renovado is not { } r)
            return ProblemasApi.Problema(StatusCodes.Status401Unauthorized, "NAO_AUTENTICADO", "Autenticação necessária");

        RefreshTokens.GravarCookie(http.Response, r.Refresh);
        return Results.Ok(LoginEndpoint.Sessao(r.Usuario, r.Tenant, tokens));
    }

    internal static bool TemCabecalhoCsrf(HttpRequest requisicao) =>
        string.Equals(requisicao.Headers[CabecalhoCsrf].ToString(), ValorCsrf, StringComparison.Ordinal);
}
