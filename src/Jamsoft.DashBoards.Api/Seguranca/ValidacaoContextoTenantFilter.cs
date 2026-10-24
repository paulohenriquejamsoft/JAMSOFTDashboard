using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;

namespace Jamsoft.DashBoards.Api.Seguranca;

/// <summary>Corpo de requisição que informa um tenant (ID_PUBLICO); comparado com o tenant efetivo.</summary>
public interface IReferenciaTenant
{
    Guid? TenantId { get; }
}

/// <summary>
/// Filtro das rotas tenant-scoped (ISO-04, CTX-03): <c>X-Tenant</c> divergente do token, ou tenant no
/// corpo/rota diferente do tenant efetivo → 403 TENANT_DIVERGENTE e registro ACESSO_CRUZADO na auditoria.
/// <c>X-Tenant</c> que não é GUID → 400 REQUISICAO_INVALIDA.
/// </summary>
public sealed class ValidacaoContextoTenantFilter : IEndpointFilter
{
    /// <summary>Nome do parâmetro de rota comparado com o tenant efetivo.</summary>
    public const string ParametroRota = "tenantId";
    public const string CodigoTenantDivergente = "TENANT_DIVERGENTE";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate next)
    {
        var http = contexto.HttpContext;
        var tenantAtual = http.RequestServices.GetRequiredService<ITenantAtual>();

        if (tenantAtual.CabecalhoTenantInvalido)
            return ProblemasApi.Problema(StatusCodes.Status400BadRequest, "REQUISICAO_INVALIDA",
                $"Cabeçalho {TenantAtual.CabecalhoTenant} inválido");

        var solicitado = tenantAtual.Divergente
            ? Guid.Parse(http.Request.Headers[TenantAtual.CabecalhoTenant].ToString())
            : TenantInformado(contexto, tenantAtual.IdPublico);
        if (solicitado is null || solicitado == tenantAtual.IdPublico) return await next(contexto);

        var auditoria = http.RequestServices.GetRequiredService<IAuditoria>();
        await auditoria.RegistrarAgoraAsync(OperacaoAuditada.AcessoCruzado, recursoIdPublico: null,
            tenantIdPublico: solicitado, tenantNome: null, ResultadoAuditoria.Negado,
            new { metodo = http.Request.Method, rota = http.Request.Path.Value, tenantDoUsuario = tenantAtual.IdPublico },
            http.RequestAborted);

        return ProblemasApi.Problema(StatusCodes.Status403Forbidden, CodigoTenantDivergente, "Acesso não permitido");
    }

    /// <summary>Primeiro tenant informado na rota ou no corpo que difere do efetivo (ou o informado, se igual).</summary>
    private static Guid? TenantInformado(EndpointFilterInvocationContext contexto, Guid? efetivo)
    {
        var informados = new List<Guid>();
        if (contexto.HttpContext.Request.RouteValues.TryGetValue(ParametroRota, out var valor)
            && Guid.TryParse(valor?.ToString(), out var daRota))
            informados.Add(daRota);
        informados.AddRange(contexto.Arguments.OfType<IReferenciaTenant>()
            .Where(c => c.TenantId is not null).Select(c => c.TenantId!.Value));

        if (informados.Count == 0) return null;
        return informados.FirstOrDefault(t => t != efetivo, informados[0]);
    }
}

public static class ValidacaoContextoTenantExtensoes
{
    /// <summary>Aplica o <see cref="ValidacaoContextoTenantFilter"/> à rota ou ao grupo.</summary>
    public static TBuilder ValidarContextoTenant<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new ValidacaoContextoTenantFilter());
}
