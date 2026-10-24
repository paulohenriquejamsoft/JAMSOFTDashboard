using System.Security.Cryptography;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

public sealed record LoginRequisicao(string? Email, string? Senha);

public sealed record TenantResumo(Guid IdPublico, string Nome);

/// <summary>Sessão aberta (AUTH-01). O refresh token vai só no cookie, nunca no corpo.</summary>
public sealed record SessaoResposta(string AccessToken, DateTimeOffset ExpiraEm, string Perfil, TenantResumo? Tenant,
    bool TrocarSenha);

/// <summary>
/// <c>POST /api/auth/login</c> (AUTH-01, AUTH-02, AUTH-07, AUTH-09): e-mail e senha → access token no corpo
/// e refresh token no cookie. E-mail inexistente e senha errada têm a mesma resposta e o mesmo custo.
/// </summary>
public static class LoginEndpoint
{
    public const string CodigoCredenciaisInvalidas = "CREDENCIAIS_INVALIDAS";
    public const string CodigoAcessoDesativado = "ACESSO_DESATIVADO";

    /// <summary>Hash de uma senha que ninguém conhece: e-mail inexistente também paga a verificação PBKDF2.</summary>
    private static readonly Lazy<string> HashFicticio = new(() =>
        new PasswordHasher<Usuario>().HashPassword(new Usuario(), RandomNumberGenerator.GetHexString(32)));

    public static IResult CredenciaisInvalidas() =>
        ProblemasApi.Problema(StatusCodes.Status401Unauthorized, CodigoCredenciaisInvalidas, "Credenciais inválidas");

    public static IResult AcessoDesativado() =>
        ProblemasApi.Problema(StatusCodes.Status403Forbidden, CodigoAcessoDesativado, "Acesso desativado");

    public static async Task<IResult> EntrarAsync(LoginRequisicao corpo, PlataformaDbContext banco,
        IPasswordHasher<Usuario> hasher, Bloqueio bloqueio, TokenService tokens, RefreshTokens refreshTokens,
        IAuditoria auditoria, HttpContext http, CancellationToken ct)
    {
        var email = Usuario.NormalizarEmail(corpo.Email ?? "");
        var senha = corpo.Senha ?? "";

        var usuario = await banco.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (usuario is null)
        {
            hasher.VerifyHashedPassword(new Usuario(), HashFicticio.Value, senha);
            return CredenciaisInvalidas();
        }

        // Durante o bloqueio nem a senha certa entra (AUTH-03).
        if (bloqueio.BloqueadoAte(usuario) is { } bloqueadoAte) return Bloqueio.Bloqueado(bloqueadoAte);

        if (hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senha) == PasswordVerificationResult.Failed)
        {
            var inicioBloqueio = await bloqueio.RegistrarFalhaAsync(usuario, ct);
            return inicioBloqueio is { } ate ? Bloqueio.Bloqueado(ate) : CredenciaisInvalidas();
        }

        var tenant = usuario.TenantId is { } tenantId
            ? await banco.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, ct)
            : null;
        // Situação só é revelada a quem acertou a senha.
        if (!usuario.Ativo || tenant is { Ativo: false }) return AcessoDesativado();

        await bloqueio.RegistrarSucessoAsync(usuario, ct);

        var sessao = await AbrirSessaoAsync(usuario, tenant, tokens, refreshTokens, http, familia: null, ct);

        if (usuario.Perfil == Perfil.SuperAdmin)
            await auditoria.RegistrarAgoraAsync(OperacaoAuditada.LoginSuperAdmin, usuario.IdPublico, null, null,
                ResultadoAuditoria.Sucesso, ct: ct, autor: new AutorAuditoria(usuario.IdPublico, usuario.Email, usuario.Perfil));

        return Results.Ok(sessao);
    }

    /// <summary>Emite o par de tokens: access token no corpo, refresh token no cookie.</summary>
    internal static async Task<SessaoResposta> AbrirSessaoAsync(Usuario usuario, Tenant? tenant, TokenService tokens,
        RefreshTokens refreshTokens, HttpContext http, Guid? familia, CancellationToken ct)
    {
        var refresh = await refreshTokens.EmitirAsync(usuario, familia, ct);
        RefreshTokens.GravarCookie(http.Response, refresh);
        return Sessao(usuario, tenant, tokens);
    }

    internal static SessaoResposta Sessao(Usuario usuario, Tenant? tenant, TokenService tokens)
    {
        var acesso = tokens.Emitir(usuario, tenant);
        return new SessaoResposta(acesso.Token, acesso.ExpiraEm, usuario.Perfil.ToString(),
            tenant is null ? null : new TenantResumo(tenant.IdPublico, tenant.Nome), usuario.TrocarSenha);
    }
}
