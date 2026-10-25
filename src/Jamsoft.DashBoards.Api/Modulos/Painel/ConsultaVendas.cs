using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Painel;

/// <summary>
/// Base de consulta dos blocos do painel: vendas do tenant efetivo (filtro global, DASH-07) na faixa do filtro e,
/// quando informado, só das empresas escolhidas (DASH-04).
/// </summary>
public sealed class ConsultaVendas(PlataformaDbContext banco)
{
    /// <summary>Vendas em <c>[DesdeUtc, AteUtcExclusivo)</c>, de qualquer situação.</summary>
    public IQueryable<Venda> Do(FiltroPainel filtro, int[]? empresaIds)
    {
        var desde = filtro.DesdeUtc;
        var ate = filtro.AteUtcExclusivo;
        var vendas = banco.Vendas.AsNoTracking().Where(v => v.DataHora >= desde && v.DataHora < ate);
        return empresaIds is { Length: > 0 } ? vendas.Where(v => empresaIds.Contains(v.EmpresaId)) : vendas;
    }

    /// <summary>
    /// Converte os ID_PUBLICO das empresas nos IDs internos. <c>Empresas</c> já vem filtrado pelo tenant: empresa de
    /// outro tenant ou inexistente → 404 NAO_ENCONTRADO (DASH-08). Nulo ou vazio → nulo (todas as empresas).
    /// </summary>
    public async Task<int[]?> ResolverEmpresasAsync(Guid[]? idsPublicos, CancellationToken ct = default)
    {
        if (idsPublicos is not { Length: > 0 }) return null;
        var distintos = idsPublicos.Distinct().ToArray();
        var ids = await banco.Empresas.AsNoTracking()
            .Where(e => distintos.Contains(e.IdPublico))
            .Select(e => e.Id)
            .ToArrayAsync(ct);
        if (ids.Length != distintos.Length)
            throw new ProblemaApiException(StatusCodes.Status404NotFound, TenantsEndpoints.CodigoNaoEncontrado,
                "Empresa não encontrada");
        return ids;
    }
}

public static class PainelExtensoes
{
    public static IServiceCollection AddPainel(this IServiceCollection services)
    {
        services.AddScoped<ConsultaVendas>();
        return services;
    }
}
