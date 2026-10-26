using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Painel;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Contratos.Mcp;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Mcp;

/// <summary>
/// Emissão de <see cref="ChaveMcp"/>, usada pela tela do tenant (<see cref="ChavesMcpEndpoints"/>) e pelo CLI de
/// implantação. No CLI não há usuário no contexto: o tenant é escolhido pelo operador e a gravação e a busca por
/// prefixo atravessam o filtro por tenant de forma explícita.
/// </summary>
public static class EmissaoChavesMcp
{
    /// <summary>Chave nova (ainda não gravada) e a chave em claro, que só existe aqui.</summary>
    public static (ChaveMcp Registro, string Chave) Nova(string descricao, DateOnly? ultimoDia)
    {
        var (chave, prefixo) = FormatoChaveMcp.Gerar();
        var registro = new ChaveMcp
        {
            Prefixo = prefixo,
            Hash = FormatoChaveMcp.Hash(chave),
            Descricao = descricao,
            Ativo = true,
            ExpiraEm = ultimoDia is { } dia ? ExpiracaoUtc(dia) : null,
        };
        return (registro, chave);
    }

    /// <summary>A chave vale até o fim de <paramref name="ultimoDia"/> em São Paulo: expira à meia-noite seguinte, em UTC.</summary>
    public static DateTime ExpiracaoUtc(DateOnly ultimoDia)
    {
        var local = ultimoDia.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, FiltroPainel.FusoSaoPaulo.GetUtcOffset(local)).UtcDateTime;
    }

    /// <summary>Inverso de <see cref="ExpiracaoUtc"/>: o último dia válido em São Paulo.</summary>
    public static DateOnly UltimoDia(DateTime expiraEmUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(expiraEmUtc, DateTimeKind.Utc),
            FiltroPainel.FusoSaoPaulo)).AddDays(-1);

    /// <summary>CLI: grava a chave nova para o <paramref name="tenant"/> e devolve a chave em claro.</summary>
    public static async Task<(string Chave, string Prefixo)> GerarAsync(PlataformaDbContext banco, Tenant tenant,
        string descricao, DateOnly? ultimoDia, CancellationToken ct = default)
    {
        var (registro, chave) = Nova(descricao, ultimoDia);
        registro.TenantId = tenant.Id;
        banco.ChavesMcp.Add(registro);
        using (banco.OperacaoGlobal())
            await banco.SaveChangesAsync(ct);
        return (chave, registro.Prefixo);
    }

    /// <summary>CLI: inativa a chave ativa do <paramref name="prefixo"/> em qualquer tenant; false se não houver.</summary>
    public static async Task<bool> RevogarAsync(PlataformaDbContext banco, string prefixo, DateTime agoraUtc,
        CancellationToken ct = default) =>
        await banco.ChavesMcp.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
            .Where(c => c.Prefixo == prefixo && c.Ativo)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.Ativo, false).SetProperty(x => x.RevogadoEm, agoraUtc), ct) > 0;
}
