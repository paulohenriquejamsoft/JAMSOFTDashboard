using Jamsoft.DashBoards.Api.Seguranca;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

/// <summary>
/// <c>POST /api/auth/logout</c> (AUTH-06): revoga a família do refresh token do cookie e apaga o cookie.
/// Idempotente: sem cookie, ou com token já revogado, responde 204 do mesmo jeito.
/// </summary>
public static class LogoutEndpoint
{
    public static async Task<IResult> SairAsync(HttpContext http, RefreshTokens refreshTokens, CancellationToken ct)
    {
        var token = http.Request.Cookies[RefreshTokens.Cookie];
        if (!string.IsNullOrEmpty(token)) await refreshTokens.RevogarFamiliaAsync(token, ct);

        RefreshTokens.ApagarCookie(http.Response);
        return Results.NoContent();
    }
}
