using System.Text.Json;
using System.Text.Json.Nodes;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jamsoft.DashBoards.Api.Modulos.Auditoria;

/// <summary>Operação registrada em AUDITORIA.OPERACAO (AUD-01). Novos módulos acrescentam as suas.</summary>
public readonly record struct OperacaoAuditada(string Codigo)
{
    /// <summary>Contexto de tenant divergente do token (ISO-04).</summary>
    public static readonly OperacaoAuditada AcessoCruzado = new("ACESSO_CRUZADO");

    /// <summary>Login bem-sucedido de SuperAdmin (AUD-01).</summary>
    public static readonly OperacaoAuditada LoginSuperAdmin = new("LOGIN_SUPERADMIN");

    /// <summary>SuperAdmin acessando rota de um tenant pelo X-Tenant (CTX-02).</summary>
    public static readonly OperacaoAuditada AcessoContextoTenant = new("ACESSO_CONTEXTO_TENANT");

    public static readonly OperacaoAuditada TenantCriado = new("TENANT_CRIADO");
    public static readonly OperacaoAuditada TenantAlterado = new("TENANT_ALTERADO");
    public static readonly OperacaoAuditada TenantInativado = new("TENANT_INATIVADO");
    public static readonly OperacaoAuditada TenantReativado = new("TENANT_REATIVADO");

    public static readonly OperacaoAuditada EmpresaCriada = new("EMPRESA_CRIADA");
    public static readonly OperacaoAuditada EmpresaAlterada = new("EMPRESA_ALTERADA");

    public static readonly OperacaoAuditada UsuarioCriado = new("USUARIO_CRIADO");
    public static readonly OperacaoAuditada UsuarioAlterado = new("USUARIO_ALTERADO");
    public static readonly OperacaoAuditada SenhaRedefinida = new("SENHA_REDEFINIDA");

    /// <summary>Ciclo da exclusão de tenant (DEL-08).</summary>
    public static readonly OperacaoAuditada ExclusaoSolicitada = new("EXCLUSAO_SOLICITADA");
    public static readonly OperacaoAuditada ExclusaoIniciada = new("EXCLUSAO_INICIADA");
    public static readonly OperacaoAuditada ExclusaoConcluida = new("EXCLUSAO_CONCLUIDA");
    public static readonly OperacaoAuditada ExclusaoFalhou = new("EXCLUSAO_FALHOU");
    public static readonly OperacaoAuditada ExclusaoRetomada = new("EXCLUSAO_RETOMADA");

    /// <summary>Credenciais do Coletor (parte Coletor do AUD-01).</summary>
    public static readonly OperacaoAuditada ColetorCriado = new("COLETOR_CRIADO");
    public static readonly OperacaoAuditada ColetorTenantsAlterados = new("COLETOR_TENANTS_ALTERADOS");
    public static readonly OperacaoAuditada ColetorRevogado = new("COLETOR_REVOGADO");
    public static readonly OperacaoAuditada ColetorSecretRotacionado = new("COLETOR_SECRET_ROTACIONADO");

    public override string ToString() => Codigo;
}

/// <summary>Autor informado explicitamente, quando ainda não há usuário autenticado na requisição (login).</summary>
public sealed record AutorAuditoria(Guid UsuarioIdPublico, string Email, Perfil Perfil);

/// <summary>Valores de AUDITORIA.RESULTADO.</summary>
public static class ResultadoAuditoria
{
    public const string Sucesso = "Sucesso";
    public const string Negado = "Negado";
    public const string Falha = "Falha";
}

/// <summary>Grava operações críticas (AUD-01/AUD-02) com autor, perfil, IP e data UTC da requisição.</summary>
public interface IAuditoria
{
    /// <summary>Adiciona o registro ao DbContext da requisição: grava junto com a operação, no mesmo SaveChanges.</summary>
    RegistroAuditoria Registrar(OperacaoAuditada operacao, Guid? recursoIdPublico, Guid? tenantIdPublico,
        string? tenantNome, string resultado, object? detalhes = null);

    /// <summary>Grava na hora, fora da transação da requisição (login, acesso negado).</summary>
    Task<RegistroAuditoria> RegistrarAgoraAsync(OperacaoAuditada operacao, Guid? recursoIdPublico, Guid? tenantIdPublico,
        string? tenantNome, string resultado, object? detalhes = null, CancellationToken ct = default,
        AutorAuditoria? autor = null);
}

public sealed class ServicoAuditoria(
    PlataformaDbContext banco,
    ITenantAtual tenantAtual,
    IHttpContextAccessor acessor,
    TimeProvider tempo,
    IServiceScopeFactory escopos) : IAuditoria
{
    /// <summary>Chaves de <c>detalhes</c> que contêm um destes trechos são removidas (AUTH-09, AUD-02).</summary>
    private static readonly string[] TrechosSigilosos = ["senha", "token", "secret"];

    public RegistroAuditoria Registrar(OperacaoAuditada operacao, Guid? recursoIdPublico, Guid? tenantIdPublico,
        string? tenantNome, string resultado, object? detalhes = null)
    {
        var registro = Criar(operacao, recursoIdPublico, tenantIdPublico, tenantNome, resultado, detalhes);
        banco.Auditoria.Add(registro);
        return registro;
    }

    public async Task<RegistroAuditoria> RegistrarAgoraAsync(OperacaoAuditada operacao, Guid? recursoIdPublico,
        Guid? tenantIdPublico, string? tenantNome, string resultado, object? detalhes = null, CancellationToken ct = default,
        AutorAuditoria? autor = null)
    {
        var registro = Criar(operacao, recursoIdPublico, tenantIdPublico, tenantNome, resultado, detalhes, autor);
        // Contexto próprio: não leva junto alterações pendentes nem depende da transação da requisição.
        await using var escopo = escopos.CreateAsyncScope();
        var outroBanco = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        outroBanco.Auditoria.Add(registro);
        await outroBanco.SaveChangesAsync(ct);
        return registro;
    }

    private RegistroAuditoria Criar(OperacaoAuditada operacao, Guid? recursoIdPublico, Guid? tenantIdPublico,
        string? tenantNome, string resultado, object? detalhes, AutorAuditoria? autor = null) => new()
        {
            OcorridoEm = tempo.GetUtcNow().UtcDateTime,
            UsuarioIdPublico = autor?.UsuarioIdPublico ?? tenantAtual.UsuarioIdPublico,
            UsuarioEmail = autor?.Email,
            Perfil = (autor?.Perfil ?? tenantAtual.Perfil)?.ToString(),
            Operacao = operacao.Codigo,
            RecursoIdPublico = recursoIdPublico,
            TenantIdPublico = tenantIdPublico,
            TenantNome = tenantNome,
            Ip = acessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Resultado = resultado,
            Detalhes = Serializar(detalhes),
        };

    private static string? Serializar(object? detalhes)
    {
        if (detalhes is null) return null;
        var json = JsonSerializer.SerializeToNode(detalhes, JsonSerializerOptions.Web);
        RemoverSigilos(json);
        return json?.ToJsonString();
    }

    private static void RemoverSigilos(JsonNode? no)
    {
        switch (no)
        {
            case JsonObject objeto:
                foreach (var chave in objeto.Select(p => p.Key).ToList())
                {
                    if (TrechosSigilosos.Any(t => chave.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        objeto.Remove(chave);
                    else
                        RemoverSigilos(objeto[chave]);
                }
                break;
            case JsonArray lista:
                foreach (var item in lista) RemoverSigilos(item);
                break;
        }
    }
}

public static class AuditoriaExtensoes
{
    public static IServiceCollection AddAuditoria(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditoria, ServicoAuditoria>();
        return services;
    }
}
