using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Tenants;

public sealed record EditarTenantRequisicao(string? Nome, string? Cnpj, string? Versao);

/// <summary>Página de resultados: <c>pagina</c> começa em 1.</summary>
public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Pagina, int Tamanho, int Total);

/// <summary>
/// Gestão de tenants em <c>/api/admin/tenants</c>: somente SuperAdmin (TEN-08). Tenant não pertence a um tenant,
/// então não passa pelo filtro global. Na API o tenant aparece só pelo ID público (TEN-07).
/// </summary>
public static class TenantsEndpoints
{
    public const string CodigoNaoEncontrado = "NAO_ENCONTRADO";
    public const string CodigoConflitoVersao = "CONFLITO_VERSAO";

    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;

    public static IEndpointRouteBuilder MapTenants(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/admin/tenants").RequireAuthorization(Policies.SuperAdmin);
        grupo.MapPost("/", CriarTenantEndpoint.CriarAsync);
        grupo.MapGet("/", ListarAsync);
        grupo.MapGet("/{idPublico}", ObterAsync);
        grupo.MapPut("/{idPublico}", EditarAsync);
        grupo.MapSituacaoTenant();
        return app;
    }

    public static IResult NaoEncontrado() =>
        ProblemasApi.Problema(StatusCodes.Status404NotFound, CodigoNaoEncontrado, "Registro não encontrado");

    /// <summary>
    /// TEN-04: <c>busca</c> procura no nome e, quando só tem dígitos e máscara, também no CNPJ;
    /// <c>situacao</c> é <c>Ativo</c> ou <c>Inativo</c>; <c>tamanho</c> é limitado a 100.
    /// </summary>
    public static async Task<IResult> ListarAsync(string? busca, string? situacao, int? pagina, int? tamanho,
        PlataformaDbContext banco, CancellationToken ct)
    {
        bool? ativo = null;
        switch (situacao?.Trim().ToLowerInvariant())
        {
            case null or "": break;
            case "ativo": ativo = true; break;
            case "inativo": ativo = false; break;
            default:
                return ProblemasApi.Validacao(new Dictionary<string, string[]> { ["situacao"] = ["Use Ativo ou Inativo."] });
        }
        var numero = Math.Max(pagina ?? 1, 1);
        var porPagina = Math.Clamp(tamanho ?? TamanhoPadrao, 1, TamanhoMaximo);

        var consulta = banco.Tenants.AsNoTracking();
        if (ativo is { } a) consulta = consulta.Where(t => t.Ativo == a);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            var digitos = string.Concat(termo.Where(char.IsAsciiDigit));
            var pareceCnpj = digitos.Length > 0 && termo.All(c => char.IsAsciiDigit(c) || c is '.' or '/' or '-' or ' ');
            consulta = pareceCnpj
                ? consulta.Where(t => t.Nome.Contains(termo) || t.Cnpj.Contains(digitos))
                : consulta.Where(t => t.Nome.Contains(termo));
        }

        var total = await consulta.CountAsync(ct);
        var itens = await consulta.OrderBy(t => t.Nome).ThenBy(t => t.Id)
            .Skip((numero - 1) * porPagina).Take(porPagina)
            .ToListAsync(ct);
        return Results.Ok(new ResultadoPaginado<TenantResposta>([.. itens.Select(TenantResposta.De)], numero, porPagina, total));
    }

    public static async Task<IResult> ObterAsync(Guid idPublico, PlataformaDbContext banco, CancellationToken ct) =>
        await banco.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.IdPublico == idPublico, ct) is { } tenant
            ? Results.Ok(TenantResposta.De(tenant))
            : NaoEncontrado();

    /// <summary>Edita nome e CNPJ. <c>versao</c> é a recebida na leitura; se o registro mudou depois, 409.</summary>
    public static async Task<IResult> EditarAsync(Guid idPublico, EditarTenantRequisicao corpo, PlataformaDbContext banco,
        IAuditoria auditoria, TimeProvider tempo, CancellationToken ct)
    {
        var erros = CriarTenantEndpoint.ValidarNomeCnpj(corpo.Nome, corpo.Cnpj, out var cnpj);
        if (!TentarLerVersao(corpo.Versao, out var versao)) erros["versao"] = ["Versão ausente ou inválida."];
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        var tenant = await banco.Tenants.SingleOrDefaultAsync(t => t.IdPublico == idPublico, ct);
        if (tenant is null) return NaoEncontrado();

        banco.Entry(tenant).Property(t => t.Versao).OriginalValue = versao;
        tenant.Nome = corpo.Nome!.Trim();
        tenant.Cnpj = cnpj;
        tenant.AlteradoEm = tempo.GetUtcNow().UtcDateTime;
        auditoria.Registrar(OperacaoAuditada.TenantAlterado, tenant.IdPublico, tenant.IdPublico, tenant.Nome,
            ResultadoAuditoria.Sucesso, new { tenant.Nome, tenant.Cnpj });

        try
        {
            await banco.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflitoVersao();
        }
        catch (DbUpdateException erro) when (CriarTenantEndpoint.IndiceUnicoViolado(erro) == CriarTenantEndpoint.IndiceCnpj)
        {
            return CriarTenantEndpoint.CnpjDuplicado();
        }
        return Results.Ok(TenantResposta.De(tenant));
    }

    public static IResult ConflitoVersao() =>
        ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoConflitoVersao,
            "Registro alterado por outra pessoa. Recarregue.");

    /// <summary>ROWVERSION em Base64 (8 bytes).</summary>
    internal static bool TentarLerVersao(string? texto, out byte[] versao)
    {
        versao = new byte[8];
        return texto is not null && Convert.TryFromBase64String(texto, versao, out var lidos) && lidos == 8;
    }
}
