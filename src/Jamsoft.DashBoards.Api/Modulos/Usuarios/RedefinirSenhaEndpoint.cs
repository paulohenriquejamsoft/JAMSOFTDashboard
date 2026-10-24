using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Usuarios;

public sealed record RedefinirSenhaRequisicao(string? SenhaTemporaria);

/// <summary>
/// <c>POST /api/usuarios/{idPublico}/redefinir-senha</c> (USR-07): grava a senha temporária (pela
/// <see cref="PoliticaSenha"/>), exige troca no próximo login, revoga os refresh tokens e invalida o
/// <see cref="StatusAcessoCache"/> do usuário. A auditoria não leva a senha.
/// </summary>
public static class RedefinirSenhaEndpoint
{
    public static async Task<IResult> RedefinirAsync(Guid idPublico, RedefinirSenhaRequisicao corpo,
        PlataformaDbContext banco, IPasswordHasher<Usuario> hasher, RefreshTokens refreshTokens, StatusAcessoCache status,
        IAuditoria auditoria, CancellationToken ct)
    {
        if (PoliticaSenha.Validar(corpo.SenhaTemporaria) is { Length: > 0 } erros)
            return ProblemasApi.Validacao(new Dictionary<string, string[]> { ["senhaTemporaria"] = erros });

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var usuario = await UsuariosEndpoints.DoTenant(banco).SingleOrDefaultAsync(u => u.IdPublico == idPublico, ct);
        if (usuario is null) return TenantsEndpoints.NaoEncontrado();

        usuario.SenhaHash = hasher.HashPassword(usuario, corpo.SenhaTemporaria!);
        usuario.TrocarSenha = true;
        var tenant = await UsuariosEndpoints.TenantAsync(banco, ct);
        auditoria.Registrar(OperacaoAuditada.SenhaRedefinida, usuario.IdPublico, tenant?.IdPublico, tenant?.Nome,
            ResultadoAuditoria.Sucesso, new { usuario.Email });
        await banco.SaveChangesAsync(ct);
        await refreshTokens.RevogarTodosAsync(usuario.Id, ct);
        await transacao.CommitAsync(ct);

        status.InvalidarUsuario(usuario.IdPublico);
        return Results.NoContent();
    }
}
