using System.Net.Mail;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Usuarios;

public sealed record CriarUsuarioRequisicao(string? Nome, string? Email, string? SenhaInicial, string? Perfil);

/// <summary>Usuário como sai da API: sem SENHA_HASH, ID interno ou TENANT_ID. <c>versao</c> é o ROWVERSION em Base64.</summary>
public sealed record UsuarioResposta(Guid IdPublico, string Nome, string Email, string Perfil, bool Ativo,
    bool TrocarSenha, DateTime? UltimoLoginEm, string Versao)
{
    public static UsuarioResposta De(Usuario u) => new(u.IdPublico, u.Nome, u.Email, u.Perfil.ToString(), u.Ativo,
        u.TrocarSenha, u.UltimoLoginEm, Convert.ToBase64String(u.Versao));
}

/// <summary>
/// Usuários do tenant em <c>/api/usuarios</c> (USR-01..USR-10), só <see cref="Policies.AdminTenant"/>: Admin do
/// tenant ou SuperAdmin com tenant selecionado. <see cref="Usuario"/> fica fora do filtro global, então toda
/// consulta aqui filtra TENANT_ID pelo <see cref="ITenantAtual"/> de forma explícita.
/// </summary>
public static class UsuariosEndpoints
{
    public const string CodigoPerfilNaoPermitido = "PERFIL_NAO_PERMITIDO";
    public const string CodigoEmailEmUso = "EMAIL_EM_USO";

    public const int NomeMaximo = 120;
    private const int EmailMaximo = 254;
    private const string IndiceEmail = "IX_USUARIO_EMAIL";

    public static IServiceCollection AddUsuarios(this IServiceCollection services)
    {
        services.AddScoped<RegrasUltimoAdmin>();
        return services;
    }

    public static IEndpointRouteBuilder MapUsuarios(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/usuarios").RequireAuthorization(Policies.AdminTenant).ValidarContextoTenant();
        grupo.MapGet("/", ListarAsync);
        grupo.MapPost("/", CriarAsync);
        grupo.MapPut("/{idPublico}", EditarUsuarioEndpoint.EditarAsync);
        grupo.MapPost("/{idPublico}/redefinir-senha", RedefinirSenhaEndpoint.RedefinirAsync);
        return app;
    }

    /// <summary>Usuários do tenant efetivo. SuperAdmin (sem tenant) nunca aparece.</summary>
    public static IQueryable<Usuario> DoTenant(PlataformaDbContext banco)
    {
        var tenantId = banco.TenantAtual.IdOuSemAcesso;
        return banco.Usuarios.Where(u => u.TenantId == tenantId);
    }

    public static async Task<IResult> ListarAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var usuarios = await DoTenant(banco).AsNoTracking().OrderBy(u => u.Nome).ThenBy(u => u.Id).ToListAsync(ct);
        return Results.Ok(usuarios.Select(UsuarioResposta.De).ToList());
    }

    /// <summary>
    /// Cria Admin ou Consulta no tenant efetivo com troca de senha obrigatória (USR-01). Outro perfil → 403
    /// (USR-02). E-mail repetido em qualquer tenant → 409 sem dizer de qual (USR-03), detectado pelo índice único.
    /// </summary>
    public static async Task<IResult> CriarAsync(CriarUsuarioRequisicao corpo, PlataformaDbContext banco,
        IPasswordHasher<Usuario> hasher, IAuditoria auditoria, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        var perfil = LerPerfil(corpo.Perfil, erros);
        if (perfil is { } p && !PerfilPermitido(p)) return PerfilNaoPermitido();

        ValidarNome(corpo.Nome, erros);
        var email = corpo.Email?.Trim() ?? "";
        if (email.Length > EmailMaximo || !MailAddress.TryCreate(email, out var endereco) || endereco.Address != email)
            erros["email"] = ["E-mail inválido."];
        if (PoliticaSenha.Validar(corpo.SenhaInicial) is { Length: > 0 } errosSenha)
            erros["senhaInicial"] = errosSenha;
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        if (banco.TenantAtual.Id is not { } tenantId) return TenantsEndpoints.NaoEncontrado();

        var usuario = new Usuario
        {
            TenantId = tenantId,
            Nome = corpo.Nome!.Trim(),
            Email = Usuario.NormalizarEmail(email),
            Perfil = perfil!.Value,
            Ativo = true,
            TrocarSenha = true,
        };
        usuario.SenhaHash = hasher.HashPassword(usuario, corpo.SenhaInicial!);

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        try
        {
            banco.Usuarios.Add(usuario);
            await banco.SaveChangesAsync(ct);

            var tenant = await TenantAsync(banco, ct);
            auditoria.Registrar(OperacaoAuditada.UsuarioCriado, usuario.IdPublico, tenant?.IdPublico, tenant?.Nome,
                ResultadoAuditoria.Sucesso, new { usuario.Email, perfil = usuario.Perfil.ToString() });
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException erro) when (EmailDuplicado(erro))
        {
            return ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoEmailEmUso, "E-mail já está em uso");
        }
        return Results.Created($"/api/usuarios/{usuario.IdPublico}", UsuarioResposta.De(usuario));
    }

    /// <summary>Perfil informado; texto que não é perfil vira erro de validação.</summary>
    internal static Perfil? LerPerfil(string? texto, Dictionary<string, string[]> erros)
    {
        if (Enum.TryParse<Perfil>(texto, ignoreCase: false, out var perfil) && Enum.IsDefined(perfil)
            && !int.TryParse(texto, out _))
            return perfil;
        erros["perfil"] = ["Use Admin ou Consulta."];
        return null;
    }

    /// <summary>Pela gestão de usuários do tenant só se criam ou atribuem Admin e Consulta (USR-02).</summary>
    internal static bool PerfilPermitido(Perfil perfil) => perfil is Perfil.Admin or Perfil.Consulta;

    internal static IResult PerfilNaoPermitido() =>
        ProblemasApi.Problema(StatusCodes.Status403Forbidden, CodigoPerfilNaoPermitido, "Perfil não permitido");

    internal static void ValidarNome(string? nome, Dictionary<string, string[]> erros)
    {
        if ((nome?.Trim() ?? "").Length is 0 or > NomeMaximo)
            erros["nome"] = [$"O nome precisa ter entre 1 e {NomeMaximo} caracteres."];
    }

    /// <summary>Tenant efetivo da requisição, para a auditoria.</summary>
    internal static Task<Tenant?> TenantAsync(PlataformaDbContext banco, CancellationToken ct)
    {
        var id = banco.TenantAtual.IdOuSemAcesso;
        return banco.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
    }

    private static bool EmailDuplicado(DbUpdateException erro) =>
        erro.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(IndiceEmail, StringComparison.Ordinal);
}
