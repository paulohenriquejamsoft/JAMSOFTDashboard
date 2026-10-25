using System.Security.Claims;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Jamsoft.DashBoards.Api.Seguranca;

/// <summary>Nomes das claims emitidas no access token.</summary>
public static class TiposClaim
{
    /// <summary>ID_PUBLICO do usuário.</summary>
    public const string Usuario = "sub";
    public const string Perfil = "perfil";
    /// <summary>ID_PUBLICO do tenant (ausente para SuperAdmin).</summary>
    public const string Tenant = "tenant";
    public const string TrocarSenha = "trocar_senha";
    /// <summary>Token de Coletor: ID_PUBLICO dos tenants autorizados e ativos na emissão.</summary>
    public const string Tenants = "tenants";
    /// <summary>Token de Coletor: <c>VERSAO_CREDENCIAL</c> na emissão.</summary>
    public const string VersaoCredencial = "cred_ver";
}

/// <summary>Quem chama e em qual tenant, resolvido uma vez por requisição (ISO-01, CTX-01, CTX-03).</summary>
public interface ITenantAtual
{
    /// <summary>Nulo quando não há usuário autenticado.</summary>
    Perfil? Perfil { get; }
    Guid? UsuarioIdPublico { get; }
    bool TrocaSenhaPendente { get; }

    /// <summary>Tenant efetivo (ID interno). Nulo sem tenant resolvido.</summary>
    int? Id { get; }
    Guid? IdPublico { get; }

    /// <summary>Valor usado pelo filtro global: sem tenant resolvido, -1 (não retorna nada).</summary>
    int IdOuSemAcesso => Id ?? -1;

    /// <summary>Admin/Consulta enviaram <c>X-Tenant</c> diferente do tenant do token (CTX-03).</summary>
    bool Divergente { get; }

    /// <summary>O cabeçalho <c>X-Tenant</c> veio com valor que não é um GUID.</summary>
    bool CabecalhoTenantInvalido { get; }

    /// <summary>Token de Coletor: ID públicos da claim <c>tenants</c>. Vazio para os demais perfis.</summary>
    IReadOnlySet<Guid> TenantsAutorizadosColetor => SemTenants;

    private static readonly IReadOnlySet<Guid> SemTenants = new HashSet<Guid>();
}

/// <summary>Sem usuário e sem tenant: usado fora de requisições (design-time, rotinas sem contexto).</summary>
public sealed class TenantSemAcesso : ITenantAtual
{
    public static readonly TenantSemAcesso Instancia = new();

    private TenantSemAcesso() { }

    public Perfil? Perfil => null;
    public Guid? UsuarioIdPublico => null;
    public bool TrocaSenhaPendente => false;
    public int? Id => null;
    public Guid? IdPublico => null;
    public bool Divergente => false;
    public bool CabecalhoTenantInvalido => false;
}

/// <summary>Converte o ID_PUBLICO do tenant no ID interno.</summary>
public interface IResolvedorTenant
{
    /// <summary>Nulo quando o tenant não existe.</summary>
    int? ObterId(Guid idPublico);
}

/// <summary>
/// Lê as claims <c>perfil</c>, <c>tenant</c>, <c>sub</c>, <c>trocar_senha</c> e o cabeçalho <c>X-Tenant</c>.
/// SuperAdmin usa o <c>X-Tenant</c> (CTX-01); Admin e Consulta usam sempre o tenant do token e um
/// <c>X-Tenant</c> diferente marca <see cref="Divergente"/> (CTX-03). Calculado na primeira leitura,
/// depois da autenticação.
/// </summary>
public sealed class TenantAtual(IHttpContextAccessor acessor, IResolvedorTenant resolvedor) : ITenantAtual
{
    public const string CabecalhoTenant = "X-Tenant";

    private Estado? _estado;

    private Estado Atual => _estado ??= Resolver();

    public Perfil? Perfil => Atual.Perfil;
    public Guid? UsuarioIdPublico => Atual.UsuarioIdPublico;
    public bool TrocaSenhaPendente => Atual.TrocaSenhaPendente;
    public int? Id => Atual.Id;
    public Guid? IdPublico => Atual.IdPublico;
    public bool Divergente => Atual.Divergente;
    public bool CabecalhoTenantInvalido => Atual.CabecalhoInvalido;
    public IReadOnlySet<Guid> TenantsAutorizadosColetor => Atual.TenantsColetor;

    private Estado Resolver()
    {
        var http = acessor.HttpContext;
        var usuario = http?.User;
        if (usuario?.Identity?.IsAuthenticated != true
            || !Enum.TryParse<Perfil>(usuario.FindFirstValue(TiposClaim.Perfil), out var perfil))
            return Estado.Anonimo;

        var usuarioIdPublico = Guid.TryParse(usuario.FindFirstValue(TiposClaim.Usuario), out var sub) ? sub : (Guid?)null;
        var trocaSenha = bool.TryParse(usuario.FindFirstValue(TiposClaim.TrocarSenha), out var trocar) && trocar;

        Guid? cabecalho = null;
        var cabecalhoInvalido = false;
        var valorCabecalho = http!.Request.Headers[CabecalhoTenant].ToString();
        if (!string.IsNullOrEmpty(valorCabecalho))
        {
            if (Guid.TryParse(valorCabecalho, out var selecionado)) cabecalho = selecionado;
            else cabecalhoInvalido = true;
        }

        Guid? idPublico;
        var divergente = false;
        var tenantsColetor = Estado.SemTenantsColetor;
        switch (perfil)
        {
            case Seguranca.Perfil.SuperAdmin:
                idPublico = cabecalho;
                break;
            case Seguranca.Perfil.Admin or Seguranca.Perfil.Consulta:
                idPublico = Guid.TryParse(usuario.FindFirstValue(TiposClaim.Tenant), out var doToken) ? doToken : null;
                divergente = cabecalho is not null && cabecalho != idPublico;
                break;
            default:
                // Coletor: sem tenant efetivo; os autorizados vêm da claim tenants (a ingestão fixa o tenant do lote).
                idPublico = null;
                tenantsColetor = usuario.FindAll(TiposClaim.Tenants)
                    .Select(c => Guid.TryParse(c.Value, out var t) ? t : Guid.Empty)
                    .Where(t => t != Guid.Empty)
                    .ToHashSet();
                break;
        }

        var id = idPublico is { } guid ? resolvedor.ObterId(guid) : null;
        return new Estado(perfil, usuarioIdPublico, trocaSenha, id, idPublico, divergente, cabecalhoInvalido, tenantsColetor);
    }

    private sealed record Estado(
        Perfil? Perfil, Guid? UsuarioIdPublico, bool TrocaSenhaPendente,
        int? Id, Guid? IdPublico, bool Divergente, bool CabecalhoInvalido, IReadOnlySet<Guid> TenantsColetor)
    {
        public static readonly IReadOnlySet<Guid> SemTenantsColetor = new HashSet<Guid>();
        public static readonly Estado Anonimo = new(null, null, false, null, null, false, false, SemTenantsColetor);
    }
}

/// <summary>Consulta o ID interno do tenant no banco e guarda em memória por 5 minutos.</summary>
public sealed class ResolvedorTenant(IServiceScopeFactory escopos, IMemoryCache cache) : IResolvedorTenant
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(5);

    public int? ObterId(Guid idPublico) =>
        cache.GetOrCreate(("tenant-id", idPublico), entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Validade;
            using var escopo = escopos.CreateScope();
            var banco = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            return banco.Tenants.AsNoTracking()
                .Where(t => t.IdPublico == idPublico)
                .Select(t => (int?)t.Id)
                .SingleOrDefault();
        });
}

public static class TenantAtualExtensoes
{
    public static IServiceCollection AddTenantAtual(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton<IResolvedorTenant, ResolvedorTenant>();
        services.AddScoped<ITenantAtual, TenantAtual>();
        return services;
    }
}
