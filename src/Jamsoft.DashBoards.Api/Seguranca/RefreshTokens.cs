using System.Security.Cryptography;
using System.Text;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;

namespace Jamsoft.DashBoards.Api.Seguranca;

public sealed record RefreshEmitido(string Token, DateTimeOffset ExpiraEm);

/// <summary>
/// Refresh tokens (AD-004, AD-010): 48 caracteres aleatórios entregues só no cookie <see cref="Cookie"/>;
/// no banco fica só o SHA-256. Cada login abre uma família nova.
/// </summary>
public sealed class RefreshTokens(PlataformaDbContext banco, TimeProvider tempo)
{
    public const string Cookie = "rt";
    public const string CaminhoCookie = "/api/auth";
    public static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int Tamanho = 48;

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>Gera e grava um token novo na <paramref name="familia"/> (nova, quando nula).</summary>
    public async Task<RefreshEmitido> EmitirAsync(Usuario usuario, Guid? familia = null, CancellationToken ct = default)
    {
        var token = RandomNumberGenerator.GetString(Alfabeto, Tamanho);
        var agora = tempo.GetUtcNow();
        var expiraEm = agora.Add(Validade);
        banco.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = usuario.Id,
            TenantId = usuario.TenantId,
            Hash = Hash(token),
            Familia = familia ?? Guid.NewGuid(),
            CriadoEm = agora.UtcDateTime,
            ExpiraEm = expiraEm.UtcDateTime,
        });
        await banco.SaveChangesAsync(ct);
        return new RefreshEmitido(token, expiraEm);
    }

    /// <summary>Cookie <c>HttpOnly; Secure; SameSite=Strict; Path=/api/auth</c>, fora do alcance de scripts.</summary>
    public static void GravarCookie(HttpResponse resposta, RefreshEmitido refresh) =>
        resposta.Cookies.Append(Cookie, refresh.Token, OpcoesCookie(refresh.ExpiraEm));

    private static CookieOptions OpcoesCookie(DateTimeOffset? expiraEm) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CaminhoCookie,
        Expires = expiraEm,
    };
}
