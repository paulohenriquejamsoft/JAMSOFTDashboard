using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Exclusao;

public sealed record SolicitarExclusaoRequisicao(string? ConfirmacaoNome);

/// <summary><c>jobId</c> é o ID público do job de exclusão.</summary>
public sealed record SolicitarExclusaoResposta(Guid JobId);

/// <summary>
/// <c>POST /api/admin/tenants/{idPublico}/exclusao</c> (DEL-01, DEL-02, DEL-03, DEL-08, DEL-09): somente SuperAdmin.
/// <c>confirmacaoNome</c> precisa ser idêntico ao nome do tenant (ordinal, sem trim). Na mesma transação inativa o
/// tenant e cria o job <see cref="SituacaoExclusao.Pendente"/>; o worker de exclusão faz o resto.
/// Job ativo já existente é detectado pelo índice único filtrado → 409.
/// </summary>
public static class SolicitarExclusaoEndpoint
{
    public const string CodigoConfirmacaoInvalida = "CONFIRMACAO_INVALIDA";
    public const string CodigoExclusaoEmAndamento = "EXCLUSAO_EM_ANDAMENTO";
    private const string IndiceJobAtivo = "IX_EXCLUSAO_TENANT_TENANT_ID";

    public static async Task<IResult> SolicitarAsync(Guid idPublico, SolicitarExclusaoRequisicao corpo,
        PlataformaDbContext banco, IAuditoria auditoria, ITenantAtual tenantAtual, StatusAcessoCache status,
        TimeProvider tempo, CancellationToken ct)
    {
        var tenant = await banco.Tenants.SingleOrDefaultAsync(t => t.IdPublico == idPublico, ct);
        if (tenant is null) return TenantsEndpoints.NaoEncontrado();

        if (!string.Equals(corpo.ConfirmacaoNome, tenant.Nome, StringComparison.Ordinal))
            return ProblemasApi.Problema(StatusCodes.Status422UnprocessableEntity, CodigoConfirmacaoInvalida,
                "O nome digitado não confere");

        var agora = tempo.GetUtcNow().UtcDateTime;
        var job = new ExclusaoTenant
        {
            TenantId = tenant.Id,
            TenantIdPublico = tenant.IdPublico,
            TenantNome = tenant.Nome,
            Situacao = SituacaoExclusao.Pendente,
            SolicitadoPorIdPublico = tenantAtual.UsuarioIdPublico!.Value,
            CriadoEm = agora,
        };
        tenant.Ativo = false;
        tenant.AlteradoEm = agora;

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        try
        {
            banco.ExclusoesTenant.Add(job);
            await banco.SaveChangesAsync(ct);

            auditoria.Registrar(OperacaoAuditada.ExclusaoSolicitada, job.IdPublico, tenant.IdPublico, tenant.Nome,
                ResultadoAuditoria.Sucesso, new { situacao = job.Situacao.ToString() });
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains(IndiceJobAtivo, StringComparison.Ordinal))
        {
            // O descarte da transação desfaz a inativação.
            return ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoExclusaoEmAndamento,
                "Exclusão já em andamento");
        }

        status.InvalidarTenant(tenant.IdPublico);
        return Results.Accepted($"/api/admin/exclusoes/{job.IdPublico}", new SolicitarExclusaoResposta(job.IdPublico));
    }
}
