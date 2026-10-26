using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Painel;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Mcp;

/// <summary><c>ExpiraEm</c>: último dia válido (inclusive, em São Paulo); nulo = não expira.</summary>
public sealed record CriarChaveMcpRequisicao(string? Descricao, DateOnly? ExpiraEm);

/// <summary>Sem a chave: ela só aparece em <see cref="ChaveMcpCriadaResposta"/>.</summary>
public sealed record ChaveMcpResposta(Guid IdPublico, string Prefixo, string Descricao, bool Ativo, DateTime CriadoEm,
    DateOnly? ExpiraEm, DateTime? RevogadoEm)
{
    public static ChaveMcpResposta De(ChaveMcp c) => new(c.IdPublico, c.Prefixo, c.Descricao, c.Ativo, c.CriadoEm,
        c.ExpiraEm is { } expira ? EmissaoChavesMcp.UltimoDia(expira) : null, c.RevogadoEm);
}

/// <summary>Resposta da criação: a api-key em claro, mostrada uma única vez.</summary>
public sealed record ChaveMcpCriadaResposta(string Chave, ChaveMcpResposta Registro);

/// <summary>
/// Api-keys do servidor MCP (conector do Claude) em <c>/api/chaves-mcp</c>, restritas ao tenant efetivo e à
/// <see cref="Policies.AdminTenant"/>. Chave de outro tenant responde 404 pelo filtro global.
/// </summary>
public static class ChavesMcpEndpoints
{
    public static IEndpointRouteBuilder MapChavesMcp(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/chaves-mcp")
            .RequireAuthorization(Policies.AdminTenant)
            .ValidarContextoTenant()
            .AuditarContextoSuperAdmin();
        grupo.MapGet("/", ListarAsync);
        grupo.MapPost("/", CriarAsync);
        grupo.MapPost("/{idPublico}/revogar", RevogarAsync);
        return app;
    }

    /// <summary>Ativas primeiro, depois as mais novas.</summary>
    public static async Task<IResult> ListarAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var chaves = await banco.ChavesMcp.AsNoTracking()
            .OrderByDescending(c => c.Ativo).ThenByDescending(c => c.CriadoEm).ThenByDescending(c => c.Id)
            .ToListAsync(ct);
        return Results.Ok(chaves.Select(ChaveMcpResposta.De).ToList());
    }

    public static async Task<IResult> CriarAsync(CriarChaveMcpRequisicao corpo, PlataformaDbContext banco,
        IAuditoria auditoria, TimeProvider tempo, CancellationToken ct)
    {
        var descricao = corpo.Descricao?.Trim() ?? "";
        var erros = new Dictionary<string, string[]>();
        if (descricao.Length is 0 or > ChaveMcp.DescricaoMaxima)
            erros["descricao"] = [$"A descrição precisa ter entre 1 e {ChaveMcp.DescricaoMaxima} caracteres."];
        if (corpo.ExpiraEm is { } expira && expira < FiltroPainel.HojeEmSaoPaulo(tempo))
            erros["expiraEm"] = ["A validade não pode ser anterior a hoje."];
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        // TENANT_ID vem do tenant efetivo (TenantSaveChangesInterceptor), nunca da requisição.
        var (registro, chave) = EmissaoChavesMcp.Nova(descricao, corpo.ExpiraEm);
        banco.ChavesMcp.Add(registro);
        await banco.SaveChangesAsync(ct);

        var tenant = await TenantAsync(banco, ct);
        auditoria.Registrar(OperacaoAuditada.ChaveMcpCriada, registro.IdPublico, tenant?.IdPublico, tenant?.Nome,
            ResultadoAuditoria.Sucesso, new { registro.Prefixo, registro.Descricao, expiraEm = corpo.ExpiraEm });
        await banco.SaveChangesAsync(ct);

        return Results.Created($"/api/chaves-mcp/{registro.IdPublico}",
            new ChaveMcpCriadaResposta(chave, ChaveMcpResposta.De(registro)));
    }

    /// <summary>Idempotente: revogar uma chave já revogada devolve o registro como está.</summary>
    public static async Task<IResult> RevogarAsync(Guid idPublico, PlataformaDbContext banco, IAuditoria auditoria,
        TimeProvider tempo, CancellationToken ct)
    {
        var registro = await banco.ChavesMcp.SingleOrDefaultAsync(c => c.IdPublico == idPublico, ct);
        if (registro is null) return TenantsEndpoints.NaoEncontrado();
        if (!registro.Ativo) return Results.Ok(ChaveMcpResposta.De(registro));

        registro.Ativo = false;
        registro.RevogadoEm = tempo.GetUtcNow().UtcDateTime;
        var tenant = await TenantAsync(banco, ct);
        auditoria.Registrar(OperacaoAuditada.ChaveMcpRevogada, registro.IdPublico, tenant?.IdPublico, tenant?.Nome,
            ResultadoAuditoria.Sucesso, new { registro.Prefixo, registro.Descricao });
        await banco.SaveChangesAsync(ct);
        return Results.Ok(ChaveMcpResposta.De(registro));
    }

    /// <summary>Tenant efetivo da requisição, para a auditoria.</summary>
    private static Task<Tenant?> TenantAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var id = banco.TenantAtual.IdOuSemAcesso;
        return banco.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
    }
}
