using System.Text.Json;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Exclusao;

/// <summary>Job de exclusão como sai da API. <c>linhasRemovidas</c>: linhas por tabela. Datas em UTC.</summary>
public sealed record ExclusaoResposta(Guid JobId, Guid TenantIdPublico, string TenantNome, string Situacao,
    string? EtapaAtual, IReadOnlyDictionary<string, long> LinhasRemovidas, string? Erro, DateTime CriadoEm,
    DateTime? IniciadoEm, DateTime? HeartbeatEm, DateTime? ConcluidoEm)
{
    public static ExclusaoResposta De(ExclusaoTenant j) => new(j.IdPublico, j.TenantIdPublico, j.TenantNome,
        j.Situacao.ToString(), j.EtapaAtual,
        string.IsNullOrEmpty(j.LinhasRemovidas) ? new Dictionary<string, long>()
            : JsonSerializer.Deserialize<Dictionary<string, long>>(j.LinhasRemovidas)!,
        j.Erro, Utc(j.CriadoEm), Utc(j.IniciadoEm), Utc(j.HeartbeatEm), Utc(j.ConcluidoEm));

    private static DateTime Utc(DateTime data) => DateTime.SpecifyKind(data, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? data) => data is { } d ? Utc(d) : null;
}

/// <summary>
/// Rotas da exclusão de tenant, somente SuperAdmin (DEL-01): solicitar (<see cref="SolicitarExclusaoEndpoint"/>),
/// acompanhar e retomar um job que falhou (DEL-06). A retomada só devolve o job a <c>Pendente</c>; o worker recomeça
/// da <c>ETAPA_ATUAL</c>.
/// </summary>
public static class ExclusoesEndpoints
{
    public const string CodigoNaoRetomavel = "EXCLUSAO_NAO_RETOMAVEL";

    public static IEndpointRouteBuilder MapExclusao(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/tenants/{idPublico}/exclusao", SolicitarExclusaoEndpoint.SolicitarAsync)
            .RequireAuthorization(Policies.SuperAdmin);

        var grupo = app.MapGroup("/api/admin/exclusoes").RequireAuthorization(Policies.SuperAdmin);
        grupo.MapGet("/{jobId}", ObterAsync);
        grupo.MapPost("/{jobId}/retomar", RetomarAsync);
        return app;
    }

    public static async Task<IResult> ObterAsync(Guid jobId, PlataformaDbContext banco, CancellationToken ct) =>
        await banco.ExclusoesTenant.AsNoTracking().SingleOrDefaultAsync(j => j.IdPublico == jobId, ct) is { } job
            ? Results.Ok(ExclusaoResposta.De(job))
            : TenantsEndpoints.NaoEncontrado();

    /// <summary>Só a partir de <c>Falhou</c> → <c>Pendente</c>, 202; outro estado → 409 EXCLUSAO_NAO_RETOMAVEL.</summary>
    public static async Task<IResult> RetomarAsync(Guid jobId, PlataformaDbContext banco, IAuditoria auditoria,
        CancellationToken ct)
    {
        var job = await banco.ExclusoesTenant.AsNoTracking().SingleOrDefaultAsync(j => j.IdPublico == jobId, ct);
        if (job is null) return TenantsEndpoints.NaoEncontrado();

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        try
        {
            // Condicional no banco: duas retomadas simultâneas não passam as duas.
            var alterados = await banco.ExclusoesTenant
                .Where(j => j.Id == job.Id && j.Situacao == SituacaoExclusao.Falhou)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Situacao, SituacaoExclusao.Pendente)
                    .SetProperty(j => j.Erro, (string?)null), ct);
            if (alterados == 0) return NaoRetomavel();

            auditoria.Registrar(OperacaoAuditada.ExclusaoRetomada, job.IdPublico, job.TenantIdPublico, job.TenantNome,
                ResultadoAuditoria.Sucesso, new { situacao = nameof(SituacaoExclusao.Pendente), etapa = job.EtapaAtual });
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (SqlException erro) when (erro.Number is 2601 or 2627)
        {
            // Outro job do mesmo tenant já está ativo (índice único filtrado).
            return ProblemasApi.Problema(StatusCodes.Status409Conflict, SolicitarExclusaoEndpoint.CodigoExclusaoEmAndamento,
                "Exclusão já em andamento");
        }
        return Results.Accepted($"/api/admin/exclusoes/{job.IdPublico}", new SolicitarExclusaoResposta(job.IdPublico));
    }

    private static IResult NaoRetomavel() =>
        ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoNaoRetomavel, "A exclusão não pode ser retomada");
}
