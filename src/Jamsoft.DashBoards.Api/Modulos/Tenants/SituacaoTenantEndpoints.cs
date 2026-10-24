using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Tenants;

/// <summary>
/// <c>POST /api/admin/tenants/{idPublico}/inativar</c> e <c>/reativar</c> (TEN-05, TEN-06): só mudam ATIVO,
/// sem tocar nos dados do tenant. O <see cref="StatusAcessoCache"/> do tenant é invalidado na hora, então
/// tokens já emitidos para usuários dele passam a receber 401 e o login, 403.
/// </summary>
public static class SituacaoTenantEndpoints
{
    public static RouteGroupBuilder MapSituacaoTenant(this RouteGroupBuilder grupo)
    {
        grupo.MapPost("/{idPublico}/inativar", (Guid idPublico, PlataformaDbContext banco, IAuditoria auditoria,
            StatusAcessoCache status, TimeProvider tempo, CancellationToken ct) =>
            AlterarAsync(idPublico, false, banco, auditoria, status, tempo, ct));
        grupo.MapPost("/{idPublico}/reativar", (Guid idPublico, PlataformaDbContext banco, IAuditoria auditoria,
            StatusAcessoCache status, TimeProvider tempo, CancellationToken ct) =>
            AlterarAsync(idPublico, true, banco, auditoria, status, tempo, ct));
        return grupo;
    }

    private static async Task<IResult> AlterarAsync(Guid idPublico, bool ativo, PlataformaDbContext banco,
        IAuditoria auditoria, StatusAcessoCache status, TimeProvider tempo, CancellationToken ct)
    {
        var tenant = await banco.Tenants.SingleOrDefaultAsync(t => t.IdPublico == idPublico, ct);
        if (tenant is null) return TenantsEndpoints.NaoEncontrado();

        tenant.Ativo = ativo;
        tenant.AlteradoEm = tempo.GetUtcNow().UtcDateTime;
        auditoria.Registrar(ativo ? OperacaoAuditada.TenantReativado : OperacaoAuditada.TenantInativado,
            tenant.IdPublico, tenant.IdPublico, tenant.Nome, ResultadoAuditoria.Sucesso);
        await banco.SaveChangesAsync(ct);

        status.InvalidarTenant(tenant.IdPublico);
        return Results.Ok(TenantResposta.De(tenant));
    }
}
