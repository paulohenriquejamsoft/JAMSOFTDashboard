using System.Net.Mail;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Tenants;

public sealed record AdminInicialRequisicao(string? Nome, string? Email, string? SenhaInicial);

public sealed record CriarTenantRequisicao(string? Nome, string? Cnpj, AdminInicialRequisicao? AdminInicial);

/// <summary>Tenant como sai da API: só o ID público (TEN-07); <c>versao</c> é o ROWVERSION em Base64.</summary>
public sealed record TenantResposta(Guid IdPublico, string Nome, string Cnpj, bool Ativo, DateTime CriadoEm,
    DateTime? AlteradoEm, string Versao)
{
    public static TenantResposta De(Tenant t) =>
        new(t.IdPublico, t.Nome, t.Cnpj, t.Ativo, t.CriadoEm, t.AlteradoEm, Convert.ToBase64String(t.Versao));
}

/// <summary>
/// <c>POST /api/admin/tenants</c> (TEN-01, TEN-02, TEN-03): cria o tenant e, opcionalmente, o primeiro Admin
/// (troca de senha obrigatória) numa única transação. CNPJ ou e-mail repetidos são detectados pelos índices
/// únicos, o que também cobre duas criações simultâneas.
/// </summary>
public static class CriarTenantEndpoint
{
    public const string CodigoCnpjDuplicado = "CNPJ_DUPLICADO";
    public const string CodigoEmailEmUso = "EMAIL_EM_USO";

    public const int NomeMinimo = 3;
    public const int NomeMaximo = 120;
    private const int EmailMaximo = 254;

    public static async Task<IResult> CriarAsync(CriarTenantRequisicao corpo, PlataformaDbContext banco,
        IPasswordHasher<Usuario> hasher, IAuditoria auditoria, CancellationToken ct)
    {
        var erros = Validar(corpo, out var cnpj);
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        var tenant = new Tenant { Nome = corpo.Nome!.Trim(), Cnpj = cnpj, Ativo = true };
        Usuario? admin = null;

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        try
        {
            banco.Tenants.Add(tenant);
            await banco.SaveChangesAsync(ct);

            if (corpo.AdminInicial is { } dados)
            {
                admin = new Usuario
                {
                    TenantId = tenant.Id,
                    Nome = dados.Nome!.Trim(),
                    Email = Usuario.NormalizarEmail(dados.Email!),
                    Perfil = Perfil.Admin,
                    Ativo = true,
                    TrocarSenha = true,
                };
                admin.SenhaHash = hasher.HashPassword(admin, dados.SenhaInicial!);
                banco.Usuarios.Add(admin);
            }

            auditoria.Registrar(OperacaoAuditada.TenantCriado, tenant.IdPublico, tenant.IdPublico, tenant.Nome,
                ResultadoAuditoria.Sucesso, new { tenant.Cnpj, adminInicial = admin?.Email });
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException erro) when (IndiceUnicoViolado(erro) is { } indice)
        {
            // O descarte da transação desfaz o tenant já inserido.
            return indice == IndiceEmail
                ? ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoEmailEmUso, "E-mail já está em uso")
                : ProblemasApi.Problema(StatusCodes.Status409Conflict, CodigoCnpjDuplicado, "CNPJ já cadastrado");
        }

        return Results.Created($"/api/admin/tenants/{tenant.IdPublico}", TenantResposta.De(tenant));
    }

    private static Dictionary<string, string[]> Validar(CriarTenantRequisicao corpo, out string cnpj)
    {
        var erros = new Dictionary<string, string[]>();
        var nome = corpo.Nome?.Trim() ?? "";
        if (nome.Length is < NomeMinimo or > NomeMaximo)
            erros["nome"] = [$"O nome precisa ter entre {NomeMinimo} e {NomeMaximo} caracteres."];
        if (!Cnpj.TentarNormalizar(corpo.Cnpj, out cnpj))
            erros["cnpj"] = ["CNPJ inválido."];

        if (corpo.AdminInicial is { } admin)
        {
            var nomeAdmin = admin.Nome?.Trim() ?? "";
            if (nomeAdmin.Length is 0 or > NomeMaximo)
                erros["adminInicial.nome"] = [$"O nome precisa ter entre 1 e {NomeMaximo} caracteres."];
            var email = admin.Email?.Trim() ?? "";
            if (email.Length > EmailMaximo || !MailAddress.TryCreate(email, out var endereco) || endereco.Address != email)
                erros["adminInicial.email"] = ["E-mail inválido."];
            if (PoliticaSenha.Validar(admin.SenhaInicial) is { Length: > 0 } errosSenha)
                erros["adminInicial.senhaInicial"] = errosSenha;
        }
        return erros;
    }

    private const string IndiceCnpj = "IX_TENANT_CNPJ";
    private const string IndiceEmail = "IX_USUARIO_EMAIL";

    /// <summary>Índice único de CNPJ ou e-mail violado (erros 2601/2627 do SQL Server), ou nulo.</summary>
    private static string? IndiceUnicoViolado(DbUpdateException erro) =>
        erro.InnerException is SqlException { Number: 2601 or 2627 } sql
            ? new[] { IndiceCnpj, IndiceEmail }.FirstOrDefault(i => sql.Message.Contains(i, StringComparison.Ordinal))
            : null;
}
