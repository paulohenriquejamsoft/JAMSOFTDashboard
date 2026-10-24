using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Usuarios;

/// <summary>
/// USR-05: o tenant nunca fica sem Admin ativo. Chamado antes de rebaixar ou inativar um Admin, dentro da
/// transação de quem chama.
/// </summary>
public sealed class RegrasUltimoAdmin(PlataformaDbContext banco)
{
    public const string CodigoUltimoAdmin = "ULTIMO_ADMIN";
    public const string TituloUltimoAdmin = "O tenant precisa de ao menos um Admin ativo";

    /// <summary>
    /// Conta os Admins ativos do tenant, sem o <paramref name="usuarioAlvoId"/>, com <c>UPDLOCK, HOLDLOCK</c>:
    /// os locks ficam até o fim da transação, então dois Admins se rebaixando ao mesmo tempo são serializados
    /// e o segundo enxerga a alteração do primeiro. Zero → 422 <see cref="CodigoUltimoAdmin"/>.
    /// </summary>
    public async Task GarantirAsync(int tenantId, int usuarioAlvoId, CancellationToken ct = default)
    {
        if (banco.Database.CurrentTransaction is null)
            throw new InvalidOperationException("RegrasUltimoAdmin.GarantirAsync precisa de uma transação aberta.");

        var outrosAdmins = await banco.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM [USUARIO] WITH (UPDLOCK, HOLDLOCK)
            WHERE [TENANT_ID] = {tenantId} AND [PERFIL] = 'Admin' AND [ATIVO] = 1 AND [ID] <> {usuarioAlvoId}
            """).SingleAsync(ct);

        if (outrosAdmins == 0)
            throw new ProblemaApiException(StatusCodes.Status422UnprocessableEntity, CodigoUltimoAdmin, TituloUltimoAdmin);
    }
}
