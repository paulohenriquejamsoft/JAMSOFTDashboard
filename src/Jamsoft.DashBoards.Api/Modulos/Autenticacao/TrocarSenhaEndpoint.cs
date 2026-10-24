using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

public sealed record TrocarSenhaRequisicao(string? SenhaAtual, string? NovaSenha);

/// <summary>Dados do usuário logado.</summary>
public sealed record EuResposta(Guid IdPublico, string Nome, string Email, string Perfil, TenantResumo? Tenant,
    bool TrocarSenha);

/// <summary>
/// <c>POST /api/auth/trocar-senha</c> (USR-08): única rota de negócio liberada com troca pendente. Confere a
/// senha atual, aplica a <see cref="PoliticaSenha"/>, zera TROCAR_SENHA e devolve um access token novo
/// (sem <c>trocar_senha</c>). <c>GET /api/auth/eu</c>: dados do usuário logado.
/// </summary>
public static class TrocarSenhaEndpoint
{
    public static async Task<IResult> TrocarAsync(TrocarSenhaRequisicao corpo, ITenantAtual atual,
        PlataformaDbContext banco, IPasswordHasher<Usuario> hasher, TokenService tokens, CancellationToken ct)
    {
        var errosPolitica = PoliticaSenha.Validar(corpo.NovaSenha);
        if (errosPolitica.Length > 0)
            return ProblemasApi.Validacao(new Dictionary<string, string[]> { ["novaSenha"] = errosPolitica });

        var usuario = await banco.Usuarios.SingleAsync(u => u.IdPublico == atual.UsuarioIdPublico, ct);
        if (hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, corpo.SenhaAtual ?? "") == PasswordVerificationResult.Failed)
            return ProblemasApi.Validacao(new Dictionary<string, string[]> { ["senhaAtual"] = ["Senha atual incorreta."] });

        usuario.SenhaHash = hasher.HashPassword(usuario, corpo.NovaSenha!);
        usuario.TrocarSenha = false;
        await banco.SaveChangesAsync(ct);

        return Results.Ok(LoginEndpoint.Sessao(usuario, await TenantAsync(banco, usuario, ct), tokens));
    }

    public static async Task<IResult> EuAsync(ITenantAtual atual, PlataformaDbContext banco, CancellationToken ct)
    {
        var usuario = await banco.Usuarios.AsNoTracking().SingleAsync(u => u.IdPublico == atual.UsuarioIdPublico, ct);
        var tenant = await TenantAsync(banco, usuario, ct);
        return Results.Ok(new EuResposta(usuario.IdPublico, usuario.Nome, usuario.Email, usuario.Perfil.ToString(),
            tenant is null ? null : new TenantResumo(tenant.IdPublico, tenant.Nome), usuario.TrocarSenha));
    }

    private static async Task<Tenant?> TenantAsync(PlataformaDbContext banco, Usuario usuario, CancellationToken ct) =>
        usuario.TenantId is { } tenantId
            ? await banco.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, ct)
            : null;
}
