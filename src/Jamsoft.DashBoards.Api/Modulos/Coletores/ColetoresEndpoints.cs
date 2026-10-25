using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Coletores;

public sealed record CriarColetorRequisicao(string? Nome, IReadOnlyList<Guid>? Tenants);

/// <summary>Resposta da criação: o <c>clientSecret</c> aparece só aqui (COL-01).</summary>
public sealed record ColetorCriadoResposta(Guid IdPublico, Guid ClientId, string ClientSecret);

public sealed record AlterarTenantsColetorRequisicao(IReadOnlyList<Guid>? Tenants, string? Versao);

/// <summary>Coletor como sai da listagem: sem secret nem hash. <c>versao</c> é o ROWVERSION em Base64.</summary>
public sealed record ColetorResposta(Guid IdPublico, string Nome, Guid ClientId, bool Ativo,
    IReadOnlyList<TenantResumo> Tenants, DateTime CriadoEm, DateTime? RevogadoEm, DateTime? UltimoTokenEm, string Versao)
{
    public static ColetorResposta De(Coletor c, IReadOnlyList<TenantResumo> tenants) =>
        new(c.IdPublico, c.Nome, c.ClientId, c.Ativo, tenants, c.CriadoEm, c.RevogadoEm, c.Acesso?.UltimoTokenEm,
            Convert.ToBase64String(c.Versao));
}

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
        grupo.MapGet("/", ListarAsync);
        grupo.MapPut("/{idPublico}/tenants", AlterarTenantsAsync);
        grupo.MapPost("/{idPublico}/revogar", RevogarAsync);
        grupo.MapPost("/{idPublico}/rotacionar", RotacionarAsync);
        return app;
    }

    public const string CodigoColetorRevogado = "COLETOR_REVOGADO";

    /// <summary>
    /// Revoga a credencial (COL-05): novos tokens são recusados e a versão nova derruba os tokens já emitidos.
    /// </summary>
    public static async Task<IResult> RevogarAsync(Guid idPublico, PlataformaDbContext banco, IAuditoria auditoria,
        StatusColetorCache status, TimeProvider tempo, CancellationToken ct)
    {
        var coletor = await banco.Coletores.Include(c => c.Acesso).SingleOrDefaultAsync(c => c.IdPublico == idPublico, ct);
        if (coletor is null) return TenantsEndpoints.NaoEncontrado();

        coletor.Ativo = false;
        coletor.RevogadoEm ??= tempo.GetUtcNow().UtcDateTime;
        coletor.VersaoCredencial++;
        auditoria.Registrar(OperacaoAuditada.ColetorRevogado, coletor.IdPublico, null, null, ResultadoAuditoria.Sucesso,
            new { coletor.Nome, coletor.ClientId });
        await banco.SaveChangesAsync(ct);
        status.Invalidar(coletor.IdPublico);

        var tenants = await TenantsDosColetoresAsync(banco, [coletor.Id], ct);
        return Results.Ok(ColetorResposta.De(coletor, tenants.GetValueOrDefault(coletor.Id, [])));
    }

    /// <summary>
    /// Gera um secret novo, exibido só nesta resposta, e invalida o anterior (COL-06). Coletor revogado → 409.
    /// </summary>
    public static async Task<IResult> RotacionarAsync(Guid idPublico, PlataformaDbContext banco, IAuditoria auditoria,
        StatusColetorCache status, CancellationToken ct)
    {
        var coletor = await banco.Coletores.SingleOrDefaultAsync(c => c.IdPublico == idPublico, ct);
        if (coletor is null) return TenantsEndpoints.NaoEncontrado();
        if (!coletor.Ativo)
            return ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoColetorRevogado, "Credencial revogada");

        var segredo = SegredoColetor.Gerar();
        coletor.SecretHash = SegredoColetor.Hash(segredo);
        coletor.VersaoCredencial++;
        auditoria.Registrar(OperacaoAuditada.ColetorSecretRotacionado, coletor.IdPublico, null, null,
            ResultadoAuditoria.Sucesso, new { coletor.Nome, coletor.ClientId });
        await banco.SaveChangesAsync(ct);
        status.Invalidar(coletor.IdPublico);

        return Results.Ok(new ColetorCriadoResposta(coletor.IdPublico, coletor.ClientId, segredo));
    }

    /// <summary>Lista paginada (<c>pagina</c> a partir de 1, <c>tamanho</c> até 100) com os tenants autorizados.</summary>
    public static async Task<IResult> ListarAsync(int? pagina, int? tamanho, PlataformaDbContext banco, CancellationToken ct)
    {
        var numero = Math.Max(pagina ?? 1, 1);
        var porPagina = Math.Clamp(tamanho ?? TenantsEndpoints.TamanhoPadrao, 1, TenantsEndpoints.TamanhoMaximo);

        var total = await banco.Coletores.CountAsync(ct);
        var coletores = await banco.Coletores.AsNoTracking().Include(c => c.Acesso).OrderBy(c => c.Nome).ThenBy(c => c.Id)
            .Skip((numero - 1) * porPagina).Take(porPagina)
            .ToListAsync(ct);
        var tenants = await TenantsDosColetoresAsync(banco, [.. coletores.Select(c => c.Id)], ct);

        return Results.Ok(new ResultadoPaginado<ColetorResposta>(
            [.. coletores.Select(c => ColetorResposta.De(c, tenants.GetValueOrDefault(c.Id, [])))], numero, porPagina, total));
    }

    /// <summary>
    /// Troca a lista de tenants autorizados (COL-07). Vale no próximo token: incrementa <c>VERSAO_CREDENCIAL</c>,
    /// o que derruba os tokens já emitidos. <c>versao</c> é a recebida na leitura; se o registro mudou depois, 409.
    /// </summary>
    public static async Task<IResult> AlterarTenantsAsync(Guid idPublico, AlterarTenantsColetorRequisicao corpo,
        PlataformaDbContext banco, IAuditoria auditoria, StatusColetorCache status, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        var tenants = await TenantsAtivosAsync(banco, corpo.Tenants, erros, ct);
        if (!TenantsEndpoints.TentarLerVersao(corpo.Versao, out var versao)) erros["versao"] = ["Versão ausente ou inválida."];
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        var coletor = await banco.Coletores.Include(c => c.Acesso).SingleOrDefaultAsync(c => c.IdPublico == idPublico, ct);
        if (coletor is null) return TenantsEndpoints.NaoEncontrado();

        using (banco.OperacaoGlobal())
        {
            var atuais = await banco.ColetoresTenant.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
                .Where(x => x.ColetorId == coletor.Id).ToListAsync(ct);
            var novos = tenants.Select(t => t.Id).ToHashSet();
            banco.ColetoresTenant.RemoveRange(atuais.Where(x => !novos.Contains(x.TenantId)));
            banco.ColetoresTenant.AddRange(novos.Except(atuais.Select(x => x.TenantId))
                .Select(id => new ColetorTenant { TenantId = id, ColetorId = coletor.Id }));

            banco.Entry(coletor).Property(c => c.Versao).OriginalValue = versao;
            coletor.VersaoCredencial++;
            auditoria.Registrar(OperacaoAuditada.ColetorTenantsAlterados, coletor.IdPublico, null, null,
                ResultadoAuditoria.Sucesso, new { tenants = tenants.Select(t => t.IdPublico) });
            try
            {
                await banco.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TenantsEndpoints.ConflitoVersao();
            }
        }
        status.Invalidar(coletor.IdPublico);

        return Results.Ok(ColetorResposta.De(coletor,
            [.. tenants.OrderBy(t => t.Nome).Select(t => new TenantResumo(t.IdPublico, t.Nome))]));
    }

    /// <summary>Tenants autorizados (ID público e nome, por nome) de cada coletor, atravessando tenants.</summary>
    private static async Task<Dictionary<int, List<TenantResumo>>> TenantsDosColetoresAsync(PlataformaDbContext banco,
        List<int> coletores, CancellationToken ct)
    {
        var linhas = await (
                from autorizado in banco.ColetoresTenant.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
                join tenant in banco.Tenants on autorizado.TenantId equals tenant.Id
                where coletores.Contains(autorizado.ColetorId)
                orderby tenant.Nome, tenant.Id
                select new { autorizado.ColetorId, tenant.IdPublico, tenant.Nome })
            .ToListAsync(ct);
        return linhas.GroupBy(l => l.ColetorId)
            .ToDictionary(g => g.Key, g => g.Select(l => new TenantResumo(l.IdPublico, l.Nome)).ToList());
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
