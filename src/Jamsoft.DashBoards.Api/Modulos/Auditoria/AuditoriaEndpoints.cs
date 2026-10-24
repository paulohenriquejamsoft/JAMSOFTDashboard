using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Auditoria;

/// <summary>Registro de auditoria como sai da API. <c>ocorridoEm</c> em UTC.</summary>
public sealed record RegistroAuditoriaResposta(long Id, DateTime OcorridoEm, Guid? UsuarioIdPublico, string? UsuarioEmail,
    string? Perfil, string Operacao, Guid? RecursoIdPublico, Guid? TenantIdPublico, string? TenantNome, string? Ip,
    string Resultado, string? Detalhes);

/// <summary>
/// <c>GET /api/admin/auditoria</c> (AUD-03): somente SuperAdmin, mais recentes primeiro. Só leitura: não há rota
/// para alterar ou excluir registros (AUD-04); o banco também nega UPDATE/DELETE à aplicação.
/// </summary>
public static class AuditoriaEndpoints
{
    public static IEndpointRouteBuilder MapAuditoria(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/auditoria", ConsultarAsync).RequireAuthorization(Policies.SuperAdmin);
        return app;
    }

    /// <summary>
    /// Filtros: período (<c>de</c>/<c>ate</c>, inclusivos), <c>tenant</c> (ID público), <c>usuario</c> (ID público ou
    /// e-mail) e <c>operacao</c>. Paginação como a de tenants (20 por padrão, máximo 100).
    /// </summary>
    public static async Task<IResult> ConsultarAsync(DateTimeOffset? de, DateTimeOffset? ate, Guid? tenant,
        string? usuario, string? operacao, int? pagina, int? tamanho, PlataformaDbContext banco, CancellationToken ct)
    {
        var numero = Math.Max(pagina ?? 1, 1);
        var porPagina = Math.Clamp(tamanho ?? TenantsEndpoints.TamanhoPadrao, 1, TenantsEndpoints.TamanhoMaximo);

        var consulta = banco.Auditoria.AsNoTracking();
        if (de is { } inicio)
        {
            var utc = inicio.UtcDateTime;
            consulta = consulta.Where(a => a.OcorridoEm >= utc);
        }
        if (ate is { } fim)
        {
            var utc = fim.UtcDateTime;
            consulta = consulta.Where(a => a.OcorridoEm <= utc);
        }
        if (tenant is { } tenantId) consulta = consulta.Where(a => a.TenantIdPublico == tenantId);
        if (!string.IsNullOrWhiteSpace(operacao))
        {
            var codigo = operacao.Trim().ToUpperInvariant();
            consulta = consulta.Where(a => a.Operacao == codigo);
        }
        if (!string.IsNullOrWhiteSpace(usuario))
        {
            if (Guid.TryParse(usuario, out var usuarioId))
                consulta = consulta.Where(a => a.UsuarioIdPublico == usuarioId);
            else
            {
                // Registros feitos com usuário autenticado guardam só o ID público; o e-mail aparece no login.
                var email = Usuario.NormalizarEmail(usuario);
                var idDoEmail = await banco.Usuarios.Where(u => u.Email == email)
                    .Select(u => (Guid?)u.IdPublico).SingleOrDefaultAsync(ct);
                consulta = consulta.Where(a => a.UsuarioEmail == email || (idDoEmail != null && a.UsuarioIdPublico == idDoEmail));
            }
        }

        var total = await consulta.CountAsync(ct);
        var registros = await consulta.OrderByDescending(a => a.OcorridoEm).ThenByDescending(a => a.Id)
            .Skip((numero - 1) * porPagina).Take(porPagina)
            .ToListAsync(ct);
        var itens = registros.Select(a => new RegistroAuditoriaResposta(a.Id, DateTime.SpecifyKind(a.OcorridoEm, DateTimeKind.Utc),
            a.UsuarioIdPublico, a.UsuarioEmail, a.Perfil, a.Operacao, a.RecursoIdPublico, a.TenantIdPublico, a.TenantNome,
            a.Ip, a.Resultado, a.Detalhes)).ToList();
        return Results.Ok(new ResultadoPaginado<RegistroAuditoriaResposta>(itens, numero, porPagina, total));
    }
}
