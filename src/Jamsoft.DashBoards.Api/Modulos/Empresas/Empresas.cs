using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Empresas;

/// <summary>Empresas do tenant usadas pela ingestão (EMP-04, EMP-05).</summary>
public interface IEmpresas
{
    /// <summary>
    /// Devolve a empresa (<paramref name="tenantId"/>, <paramref name="codEmpresa"/>), criando-a com
    /// <paramref name="nomeRecebido"/> quando não existe. Empresa com nome automático recebe o nome novo;
    /// com nome editado por um usuário (NOME_MANUAL = 1) mantém o nome. Grava na hora (SaveChanges).
    /// </summary>
    Task<Empresa> ObterOuCriarAsync(int tenantId, int codEmpresa, string nomeRecebido, CancellationToken ct = default);
}

/// <summary>
/// Implementação sobre o <see cref="PlataformaDbContext"/> do escopo. O contexto precisa estar no tenant
/// <c>tenantId</c>: quem chama (a ingestão do Coletor) resolve o tenant do lote, confere que a credencial
/// pode enviar para ele e cria um escopo de DI cujo <c>ITenantAtual</c> é esse tenant. Assim o filtro global
/// e o <see cref="TenantSaveChangesInterceptor"/> continuam valendo, sem <c>OperacaoGlobal</c>. Contexto em
/// outro tenant lança <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class ServicoEmpresas(PlataformaDbContext banco) : IEmpresas
{
    private const string IndiceCodigo = "IX_EMPRESA_TENANT_ID_COD_EMPRESA";

    public async Task<Empresa> ObterOuCriarAsync(int tenantId, int codEmpresa, string nomeRecebido,
        CancellationToken ct = default)
    {
        if (banco.TenantAtual.Id != tenantId)
            throw new InvalidOperationException(
                $"O contexto está no tenant {banco.TenantAtual.Id?.ToString() ?? "(nenhum)"}, não no tenant {tenantId}.");

        var nome = NomeValido(nomeRecebido, codEmpresa);
        var empresa = await LerAsync(codEmpresa, ct);
        if (empresa is null)
        {
            empresa = new Empresa { TenantId = tenantId, CodEmpresa = codEmpresa, Nome = nome, NomeManual = false };
            banco.Empresas.Add(empresa);
            try
            {
                await banco.SaveChangesAsync(ct);
                return empresa;
            }
            catch (DbUpdateException erro) when (CodigoDuplicado(erro))
            {
                // Outra requisição criou a mesma empresa entre a leitura e a inserção: usa a que ficou gravada.
                banco.Entry(empresa).State = EntityState.Detached;
                empresa = await LerAsync(codEmpresa, ct)
                    ?? throw new InvalidOperationException($"Empresa {codEmpresa} duplicada, mas não encontrada.", erro);
            }
        }

        if (!empresa.NomeManual && empresa.Nome != nome)
        {
            // Condição no próprio UPDATE: uma renomeação manual feita no meio do caminho não é sobrescrita.
            var alteradas = await banco.Empresas
                .Where(e => e.Id == empresa.Id && !e.NomeManual)
                .ExecuteUpdateAsync(e => e.SetProperty(x => x.Nome, nome), ct);
            if (alteradas == 1) empresa.Nome = nome;
        }
        return empresa;
    }

    private Task<Empresa?> LerAsync(int codEmpresa, CancellationToken ct) =>
        banco.Empresas.AsNoTracking().SingleOrDefaultAsync(e => e.CodEmpresa == codEmpresa, ct);

    /// <summary>Nome do ERP sem espaços nas pontas e cortado em 80 caracteres; vazio vira "Empresa {código}".</summary>
    private static string NomeValido(string? nomeRecebido, int codEmpresa)
    {
        var nome = nomeRecebido?.Trim() ?? "";
        if (nome.Length == 0) return $"Empresa {codEmpresa}";
        return nome.Length > Empresa.NomeMaximo ? nome[..Empresa.NomeMaximo].TrimEnd() : nome;
    }

    private static bool CodigoDuplicado(DbUpdateException erro) =>
        erro.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(IndiceCodigo, StringComparison.Ordinal);
}

public static class EmpresasExtensoes
{
    public static IServiceCollection AddEmpresas(this IServiceCollection services)
    {
        services.AddScoped<IEmpresas, ServicoEmpresas>();
        return services;
    }
}
