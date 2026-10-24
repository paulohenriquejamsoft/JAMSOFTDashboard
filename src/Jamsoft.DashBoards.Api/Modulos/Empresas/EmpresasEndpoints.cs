using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Empresas;

public sealed record CriarEmpresaRequisicao(int? CodEmpresa, string? Nome);

public sealed record RenomearEmpresaRequisicao(string? Nome);

public sealed record EmpresaResposta(Guid IdPublico, int CodEmpresa, string Nome, bool NomeManual)
{
    public static EmpresaResposta De(Empresa e) => new(e.IdPublico, e.CodEmpresa, e.Nome, e.NomeManual);
}

/// <summary>
/// Empresas do tenant em <c>/api/empresas</c> (EMP-02, EMP-03, EMP-05, EMP-06): leitura com
/// <see cref="Policies.LeituraTenant"/>, escrita com <see cref="Policies.AdminTenant"/>. O filtro global restringe
/// tudo ao tenant efetivo: empresa de outro tenant responde 404.
/// </summary>
public static class EmpresasEndpoints
{
    public const string CodigoEmpresaDuplicada = "EMPRESA_DUPLICADA";

    public static IEndpointRouteBuilder MapEmpresas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/empresas").ValidarContextoTenant();
        grupo.MapGet("/", ListarAsync).RequireAuthorization(Policies.LeituraTenant);
        grupo.MapPost("/", CriarAsync).RequireAuthorization(Policies.AdminTenant);
        grupo.MapPut("/{idPublico}", RenomearAsync).RequireAuthorization(Policies.AdminTenant);
        return app;
    }

    public static async Task<IResult> ListarAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var empresas = await banco.Empresas.AsNoTracking().OrderBy(e => e.CodEmpresa).ToListAsync(ct);
        return Results.Ok(empresas.Select(EmpresaResposta.De).ToList());
    }

    public static async Task<IResult> CriarAsync(CriarEmpresaRequisicao corpo, PlataformaDbContext banco,
        IAuditoria auditoria, CancellationToken ct)
    {
        var erros = ValidarNome(corpo.Nome);
        if (corpo.CodEmpresa is not > 0) erros["codEmpresa"] = ["O código da empresa precisa ser um inteiro positivo."];
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        // TENANT_ID vem do tenant efetivo (TenantSaveChangesInterceptor), nunca da requisição.
        var empresa = new Empresa { CodEmpresa = corpo.CodEmpresa!.Value, Nome = corpo.Nome!.Trim(), NomeManual = true };
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        try
        {
            banco.Empresas.Add(empresa);
            await banco.SaveChangesAsync(ct);

            var tenant = await TenantAsync(banco, ct);
            auditoria.Registrar(OperacaoAuditada.EmpresaCriada, empresa.IdPublico, tenant?.IdPublico, tenant?.Nome,
                ResultadoAuditoria.Sucesso, new { empresa.CodEmpresa, empresa.Nome });
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException erro) when (ServicoEmpresas.CodigoDuplicado(erro))
        {
            return ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoEmpresaDuplicada,
                "Já existe empresa com este código");
        }
        return Results.Created($"/api/empresas/{empresa.IdPublico}", EmpresaResposta.De(empresa));
    }

    /// <summary>Renomear marca NOME_MANUAL = 1: a ingestão deixa de sobrescrever o nome (EMP-05).</summary>
    public static async Task<IResult> RenomearAsync(Guid idPublico, RenomearEmpresaRequisicao corpo,
        PlataformaDbContext banco, IAuditoria auditoria, CancellationToken ct)
    {
        var erros = ValidarNome(corpo.Nome);
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        var empresa = await banco.Empresas.SingleOrDefaultAsync(e => e.IdPublico == idPublico, ct);
        if (empresa is null) return TenantsEndpoints.NaoEncontrado();

        empresa.Nome = corpo.Nome!.Trim();
        empresa.NomeManual = true;
        var tenant = await TenantAsync(banco, ct);
        auditoria.Registrar(OperacaoAuditada.EmpresaAlterada, empresa.IdPublico, tenant?.IdPublico, tenant?.Nome,
            ResultadoAuditoria.Sucesso, new { empresa.CodEmpresa, empresa.Nome });
        await banco.SaveChangesAsync(ct);
        return Results.Ok(EmpresaResposta.De(empresa));
    }

    private static Dictionary<string, string[]> ValidarNome(string? nome)
    {
        var erros = new Dictionary<string, string[]>();
        if ((nome?.Trim() ?? "").Length is 0 or > Empresa.NomeMaximo)
            erros["nome"] = [$"O nome precisa ter entre 1 e {Empresa.NomeMaximo} caracteres."];
        return erros;
    }

    /// <summary>Tenant efetivo da requisição, para a auditoria.</summary>
    private static Task<Tenant?> TenantAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var id = banco.TenantAtual.IdOuSemAcesso;
        return banco.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
    }
}
