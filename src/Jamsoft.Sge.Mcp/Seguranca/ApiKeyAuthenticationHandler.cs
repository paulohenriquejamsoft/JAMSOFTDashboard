using System.Security.Claims;
using System.Text.Encodings.Web;
using Jamsoft.Sge.Mcp.Configuracao;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Jamsoft.Sge.Mcp.Seguranca;

public static class ApiKeyDefaults
{
    public const string Esquema = "ApiKey";
    public const string Cabecalho = "X-Api-Key";
    public const string ParametroQuery = "api_key";
    /// <summary>ID_PUBLICO do tenant, com o mesmo nome da claim do access token da API.</summary>
    public const string ClaimTenant = "tenant";
}

/// <summary>
/// Lê a api-key da requisição, valida no banco da plataforma e coloca o tenant no HttpContext.
/// Aceita, nesta ordem:
///   1. X-Api-Key: sge_xxx
///   2. Authorization: Bearer sge_xxx
///   3. ?api_key=sge_xxx  (somente se Mcp:PermitirChaveNaQuery = true)
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IRepositorioChaves repositorio,
    IOptions<McpOptions> mcpOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var chave = LerChave();
        if (chave is null) return AuthenticateResult.NoResult();

        var tenant = await repositorio.ValidarAsync(chave, Context.RequestAborted);
        if (tenant is null)
        {
            Logger.LogWarning("api-key recusada vinda de {Ip}.", Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail("api-key inválida, revogada, vencida ou de tenant inativo.");
        }

        Context.DefinirTenant(tenant);

        var identidade = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, tenant.PrefixoChave),
            new Claim(ClaimTypes.Name, tenant.Nome),
            new Claim(ApiKeyDefaults.ClaimTenant, tenant.IdPublico.ToString()),
        ], ApiKeyDefaults.Esquema);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identidade), ApiKeyDefaults.Esquema));
    }

    private string? LerChave()
    {
        var cabecalho = Request.Headers[ApiKeyDefaults.Cabecalho].ToString();
        if (!string.IsNullOrWhiteSpace(cabecalho)) return cabecalho.Trim();

        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();

        if (mcpOptions.Value.PermitirChaveNaQuery)
        {
            var query = Request.Query[ApiKeyDefaults.ParametroQuery].ToString();
            if (!string.IsNullOrWhiteSpace(query)) return query.Trim();
        }

        return null;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{ApiKeyDefaults.Esquema} header=\"{ApiKeyDefaults.Cabecalho}\"";
        await Response.WriteAsJsonAsync(new
        {
            erro = "nao_autorizado",
            mensagem = $"Envie uma api-key válida no cabeçalho {ApiKeyDefaults.Cabecalho} " +
                       "ou em Authorization: Bearer <chave>."
        });
    }
}
