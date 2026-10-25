using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Coletores;

public sealed record CriarColetorRequisicao(string? Nome, IReadOnlyList<Guid>? Tenants);

/// <summary>Resposta da criação: o <c>clientSecret</c> aparece só aqui (COL-01).</summary>
public sealed record ColetorCriadoResposta(Guid IdPublico, Guid ClientId, string ClientSecret);

/// <summary>
/// Gestão de credenciais do Coletor em <c>/api/admin/coletores</c>: somente SuperAdmin (COL-08).
/// <see cref="ColetorTenant"/> pertence a cada tenant, mas aqui a gestão atravessa tenants: as gravações
/// rodam em <see cref="PlataformaDbContext.OperacaoGlobal"/> (módulo na allowlist de bypass).
/// </summary>
public static class ColetoresEndpoints
{
    public const int NomeMinimo = 3;

    public static IEndpointRouteBuilder MapColetores(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/admin/coletores").RequireAuthorization(Policies.SuperAdmin);
        grupo.MapPost("/", CriarAsync);
        return app;
    }

    /// <summary>
    /// Cria o coletor com <c>client_id</c> GUID e <c>client_secret</c> novo (COL-01); grava só o hash (COL-02)
    /// e a lista de tenants autorizados, todos ativos e ao menos um.
    /// </summary>
    public static async Task<IResult> CriarAsync(CriarColetorRequisicao corpo, PlataformaDbContext banco,
        IAuditoria auditoria, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        var nome = corpo.Nome?.Trim() ?? "";
        if (nome.Length is < NomeMinimo or > Coletor.NomeMaximo)
            erros["nome"] = [$"O nome precisa ter entre {NomeMinimo} e {Coletor.NomeMaximo} caracteres."];
        var tenants = await TenantsAtivosAsync(banco, corpo.Tenants, erros, ct);
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        var segredo = SegredoColetor.Gerar();
        var coletor = new Coletor { Nome = nome, ClientId = Guid.NewGuid(), SecretHash = SegredoColetor.Hash(segredo) };

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        using (banco.OperacaoGlobal())
        {
            banco.Coletores.Add(coletor);
            await banco.SaveChangesAsync(ct);

            banco.ColetoresTenant.AddRange(tenants.Select(t => new ColetorTenant { TenantId = t.Id, ColetorId = coletor.Id }));
            auditoria.Registrar(OperacaoAuditada.ColetorCriado, coletor.IdPublico, null, null, ResultadoAuditoria.Sucesso,
                new { coletor.Nome, coletor.ClientId, tenants = tenants.Select(t => t.IdPublico) });
            await banco.SaveChangesAsync(ct);
        }
        await transacao.CommitAsync(ct);

        return Results.Created($"/api/admin/coletores/{coletor.IdPublico}",
            new ColetorCriadoResposta(coletor.IdPublico, coletor.ClientId, segredo));
    }

    /// <summary>
    /// Tenants informados, sem repetição, que existem e estão ativos. Lista vazia, tenant inexistente ou inativo
    /// acrescentam o erro de <c>tenants</c>.
    /// </summary>
    internal static async Task<List<Tenant>> TenantsAtivosAsync(PlataformaDbContext banco, IReadOnlyList<Guid>? informados,
        Dictionary<string, string[]> erros, CancellationToken ct)
    {
        var ids = (informados ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            erros["tenants"] = ["Informe ao menos um tenant ativo."];
            return [];
        }

        var ativos = await banco.Tenants.AsNoTracking().Where(t => ids.Contains(t.IdPublico) && t.Ativo).ToListAsync(ct);
        if (ativos.Count != ids.Count) erros["tenants"] = ["Só tenants ativos podem ser autorizados."];
        return ativos;
    }
}
