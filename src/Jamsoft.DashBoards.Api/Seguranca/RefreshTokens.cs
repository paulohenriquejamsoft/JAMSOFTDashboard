using System.Security.Cryptography;
using System.Text;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Troca um refresh token válido por um novo da mesma família (AUTH-04). Token já usado ou revogado
    /// indica roubo: revoga todos os tokens do usuário (AUTH-05). Devolve o usuário dono do token novo,
    /// ou nulo quando a renovação é recusada (401).
    /// </summary>
    public async Task<(Usuario Usuario, Tenant? Tenant, RefreshEmitido Refresh)?> RenovarAsync(string token,
        CancellationToken ct = default)
    {
        var hash = Hash(token);
        var registro = await banco.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(r => r.Hash == hash, ct);
        if (registro is null || !CryptographicOperations.FixedTimeEquals(registro.Hash, hash)) return null;

        var agora = tempo.GetUtcNow().UtcDateTime;
        if (registro.UsadoEm is not null || registro.RevogadoEm is not null)
        {
            await RevogarTodosAsync(registro.UsuarioId, ct);
            return null;
        }
        if (registro.ExpiraEm <= agora) return null;

        var usuario = await banco.Usuarios.AsNoTracking().SingleAsync(u => u.Id == registro.UsuarioId, ct);
        var tenant = usuario.TenantId is { } tenantId
            ? await banco.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, ct)
            : null;
        if (!usuario.Ativo || tenant is { Ativo: false }) return null;

        // Marca o uso só se ninguém usou antes: duas renovações simultâneas do mesmo token = reuso.
        var marcados = await banco.RefreshTokens
            .Where(r => r.Id == registro.Id && r.UsadoEm == null && r.RevogadoEm == null)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.UsadoEm, agora), ct);
        if (marcados == 0)
        {
            await RevogarTodosAsync(registro.UsuarioId, ct);
            return null;
        }

        return (usuario, tenant, await EmitirAsync(usuario, registro.Familia, ct));
    }

    /// <summary>Revoga todos os refresh tokens ainda não revogados do usuário.</summary>
    public Task<int> RevogarTodosAsync(int usuarioId, CancellationToken ct = default)
    {
        var agora = tempo.GetUtcNow().UtcDateTime;
        return banco.RefreshTokens.Where(r => r.UsuarioId == usuarioId && r.RevogadoEm == null)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.RevogadoEm, agora), ct);
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
