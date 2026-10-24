using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Usuarios;

public sealed record EditarUsuarioRequisicao(string? Nome, string? Perfil, bool? Ativo, string? Versao);

/// <summary>
/// <c>PUT /api/usuarios/{idPublico}</c> (USR-04, USR-05, USR-06): altera nome, perfil (Admin/Consulta) e situação
/// de um usuário do tenant efetivo. Rebaixar ou inativar um Admin passa por <see cref="RegrasUltimoAdmin"/> na
/// mesma transação. Inativar revoga os refresh tokens e invalida o <see cref="StatusAcessoCache"/> do usuário.
/// </summary>
public static class EditarUsuarioEndpoint
{
    public static async Task<IResult> EditarAsync(Guid idPublico, EditarUsuarioRequisicao corpo, PlataformaDbContext banco,
        RegrasUltimoAdmin regras, RefreshTokens refreshTokens, StatusAcessoCache status, IAuditoria auditoria,
        CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        var perfil = UsuariosEndpoints.LerPerfil(corpo.Perfil, erros);
        if (perfil is { } p && !UsuariosEndpoints.PerfilPermitido(p)) return UsuariosEndpoints.PerfilNaoPermitido();

        UsuariosEndpoints.ValidarNome(corpo.Nome, erros);
        if (corpo.Ativo is null) erros["ativo"] = ["Informe a situação."];
        if (!TentarLerVersao(corpo.Versao, out var versao)) erros["versao"] = ["Versão ausente ou inválida."];
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var usuario = await UsuariosEndpoints.DoTenant(banco).SingleOrDefaultAsync(u => u.IdPublico == idPublico, ct);
        if (usuario is null) return TenantsEndpoints.NaoEncontrado();

        var novoPerfil = perfil!.Value;
        var ativo = corpo.Ativo!.Value;
        if (usuario is { Perfil: Perfil.Admin, Ativo: true } && (novoPerfil != Perfil.Admin || !ativo))
            await regras.GarantirAsync(usuario.TenantId!.Value, usuario.Id, ct);

        var inativando = usuario.Ativo && !ativo;
        banco.Entry(usuario).Property(u => u.Versao).OriginalValue = versao;
        usuario.Nome = corpo.Nome!.Trim();
        usuario.Perfil = novoPerfil;
        usuario.Ativo = ativo;

        var tenant = await UsuariosEndpoints.TenantAsync(banco, ct);
        auditoria.Registrar(OperacaoAuditada.UsuarioAlterado, usuario.IdPublico, tenant?.IdPublico, tenant?.Nome,
            ResultadoAuditoria.Sucesso, new { usuario.Nome, perfil = usuario.Perfil.ToString(), usuario.Ativo });
        try
        {
            await banco.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TenantsEndpoints.ConflitoVersao();
        }

        if (inativando) await refreshTokens.RevogarTodosAsync(usuario.Id, ct);
        await transacao.CommitAsync(ct);

        // Depois do commit: a próxima requisição com token ainda válido já relê a situação (401).
        if (inativando) status.InvalidarUsuario(usuario.IdPublico);
        return Results.Ok(UsuarioResposta.De(usuario));
    }

    /// <summary>ROWVERSION em Base64 (8 bytes).</summary>
    private static bool TentarLerVersao(string? texto, out byte[] versao)
    {
        versao = new byte[8];
        return texto is not null && Convert.TryFromBase64String(texto, versao, out var lidos) && lidos == 8;
    }
}
