using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>Uma origem do tenant com a última execução reportada (STS-01). Datas em UTC.</summary>
public sealed record OrigemSincronizacaoResposta(Guid IdPublico, Guid ConfiguracaoId, string Rotina, string? BancoOrigem,
    DateTime? UltimoSucessoEm, string? UltimaSituacao, string? UltimaMensagem, DateTime? UltimaExecucaoInicio,
    DateTime? UltimaExecucaoFim, int? RegistrosUltima);

/// <summary>Última sincronização bem-sucedida do tenant (STS-02); nula quando ainda não houve.</summary>
public sealed record ResumoSincronizacaoResposta(DateTime? UltimoSucessoEm);

/// <summary>
/// Status de sincronização para o painel em <c>/api/sincronizacao</c>, sempre no tenant efetivo (filtro global):
/// a lista das origens exige <see cref="Policies.AdminTenant"/> (STS-01); o resumo, <see cref="Policies.LeituraTenant"/>
/// (STS-02), traz o menor <c>ULTIMO_SUCESSO_EM</c> entre as origens que já tiveram sucesso.
/// </summary>
public static class SincronizacaoEndpoints
{
    public static IEndpointRouteBuilder MapSincronizacao(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/sincronizacao").ValidarContextoTenant().AuditarContextoSuperAdmin();
        grupo.MapGet("/", ListarAsync).RequireAuthorization(Policies.AdminTenant);
        grupo.MapGet("/resumo", ResumoAsync).RequireAuthorization(Policies.LeituraTenant);
        return app;
    }

    public static async Task<IResult> ListarAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var origens = await banco.OrigensSincronizacao.AsNoTracking()
            .OrderBy(o => o.Rotina).ThenBy(o => o.BancoOrigem).ThenBy(o => o.Id)
            .ToListAsync(ct);
        return Results.Ok(origens.Select(o => new OrigemSincronizacaoResposta(o.IdPublico, o.ConfiguracaoId, o.Rotina,
            o.BancoOrigem, Utc(o.UltimoSucessoEm), o.UltimaSituacao, o.UltimaMensagem, Utc(o.UltimaExecucaoInicio),
            Utc(o.UltimaExecucaoFim), o.RegistrosUltima)).ToList());
    }

    public static async Task<IResult> ResumoAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var menor = await banco.OrigensSincronizacao.MinAsync(o => o.UltimoSucessoEm, ct);
        return Results.Ok(new ResumoSincronizacaoResposta(Utc(menor)));
    }

    /// <summary>As colunas guardam UTC sem fuso; marcado para o JSON sair com "Z".</summary>
    private static DateTime? Utc(DateTime? data) => data is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
}
