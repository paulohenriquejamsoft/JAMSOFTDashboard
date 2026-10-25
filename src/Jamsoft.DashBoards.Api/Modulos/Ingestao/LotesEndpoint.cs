using System.Text.Json;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>
/// Tenant do lote como tenant atual: o Coletor não tem tenant efetivo, então a ingestão monta um contexto fixo no
/// tenant do lote depois de conferir a autorização. O filtro global e o <see cref="TenantSaveChangesInterceptor"/>
/// seguem valendo, sem <c>OperacaoGlobal</c>.
/// </summary>
public sealed class TenantDoLote(int id, Guid idPublico, Guid? coletorIdPublico) : ITenantAtual
{
    public Perfil? Perfil => Seguranca.Perfil.Coletor;
    public Guid? UsuarioIdPublico { get; } = coletorIdPublico;
    public bool TrocaSenhaPendente => false;
    public int? Id { get; } = id;
    public Guid? IdPublico { get; } = idPublico;
    public bool Divergente => false;
    public bool CabecalhoTenantInvalido => false;
}

/// <summary>
/// <c>POST /api/coletor/vendas/lotes</c> (policy <see cref="Policies.Coletor"/>; ING-01, ING-02, ING-03, ING-06,
/// ING-08): corpo acima de 2 MB ou mais de 500 vendas → 413 <c>LOTE_GRANDE_DEMAIS</c>; tenant fora da claim
/// <c>tenants</c> → 403 <c>TENANT_NAO_AUTORIZADO</c>; tenant inativo → 403 <c>TENANT_INATIVO</c>. Depois disso o
/// lote é gravado num contexto fixo no tenant dele: vendas inválidas são rejeitadas com o motivo e as demais gravadas
/// pelo <see cref="GravadorVendas"/>.
/// </summary>
public static class LotesEndpoint
{
    public const string Rota = "/api/coletor/vendas/lotes";

    public const string CodigoLoteGrandeDemais = "LOTE_GRANDE_DEMAIS";
    public const string CodigoTenantNaoAutorizado = "TENANT_NAO_AUTORIZADO";
    public const string CodigoTenantInativo = "TENANT_INATIVO";

    private const string IndiceOrigem = "IX_ORIGEM_SINCRONIZACAO_TENANT_ID_CONFIGURACAO_ID_ROTINA";

    public static IEndpointRouteBuilder MapIngestao(this IEndpointRouteBuilder app)
    {
        app.MapPost(Rota, ReceberAsync).RequireAuthorization(Policies.Coletor);
        return app;
    }

    public static IResult LoteGrandeDemais() =>
        ProblemasApi.Problema(StatusCodes.Status413PayloadTooLarge, CodigoLoteGrandeDemais,
            $"Lote acima de {Limites.VendasPorLote} vendas ou {Limites.BytesPorLote / (1024 * 1024)} MB");

    public static IResult TenantNaoAutorizado() =>
        ProblemasApi.Problema(StatusCodes.Status403Forbidden, CodigoTenantNaoAutorizado, "Tenant não autorizado para o Coletor");

    public static IResult TenantInativo() =>
        ProblemasApi.Problema(StatusCodes.Status403Forbidden, CodigoTenantInativo, "Tenant inativo");

    public static async Task<IResult> ReceberAsync(HttpContext http, ITenantAtual atual, StatusAcessoCache status,
        IResolvedorTenant resolvedor, DbContextOptions<PlataformaDbContext> opcoes, CancellationToken ct)
    {
        var (lote, erro) = await LerAsync(http.Request, ct);
        if (erro is not null) return erro;
        if (lote!.Vendas?.Count > Limites.VendasPorLote) return LoteGrandeDemais();

        var erros = Validar(lote);
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        if (!atual.TenantsAutorizadosColetor.Contains(lote.Tenant)) return TenantNaoAutorizado();
        if (!await status.TenantAtivoAsync(lote.Tenant, ct) || resolvedor.ObterId(lote.Tenant) is not { } tenantId)
            return TenantInativo();

        await using var banco = new PlataformaDbContext(opcoes, new TenantDoLote(tenantId, lote.Tenant, atual.UsuarioIdPublico));
        var resultado = await GravarAsync(banco, tenantId, atual.UsuarioIdPublico, lote, ct);
        return Results.Ok(resultado);
    }

    private static async Task<ResultadoLote> GravarAsync(PlataformaDbContext banco, int tenantId, Guid? coletorIdPublico,
        LoteVendas lote, CancellationToken ct)
    {
        var validacao = ValidacaoVenda.Validar(lote.Vendas);
        var origem = await OrigemAsync(banco, tenantId, coletorIdPublico, lote, ct);

        var empresas = new Dictionary<int, int>();
        var servico = new ServicoEmpresas(banco);
        foreach (var grupo in validacao.Validas.GroupBy(v => v.CodEmpresa))
            empresas[grupo.Key] = (await servico.ObterOuCriarAsync(tenantId, grupo.Key, grupo.First().NomeEmpresa, ct)).Id;

        var (inseridos, atualizados) = await GravadorVendas.GravarAsync(banco, tenantId, origem.Id, validacao.Validas, empresas, ct);
        return new ResultadoLote(lote.BatchId, inseridos, atualizados, validacao.Rejeitadas);
    }

    /// <summary>A origem (tenant, configuração, rotina) do lote, criada no primeiro lote.</summary>
    private static async Task<OrigemSincronizacao> OrigemAsync(PlataformaDbContext banco, int tenantId,
        Guid? coletorIdPublico, LoteVendas lote, CancellationToken ct)
    {
        var existente = await LerOrigemAsync(banco, lote, ct);
        if (existente is not null) return existente;

        var coletorId = await banco.Coletores.Where(c => c.IdPublico == coletorIdPublico).Select(c => c.Id).SingleAsync(ct);
        var origem = new OrigemSincronizacao
        {
            TenantId = tenantId,
            ColetorId = coletorId,
            ConfiguracaoId = lote.ConfiguracaoId,
            Rotina = lote.Rotina,
            BancoOrigem = Cortar(lote.BancoOrigem, OrigemSincronizacao.BancoOrigemMaximo),
        };
        banco.OrigensSincronizacao.Add(origem);
        try
        {
            await banco.SaveChangesAsync(ct);
            return origem;
        }
        catch (DbUpdateException erro) when (erro.InnerException is SqlException { Number: 2601 or 2627 } sql
                                              && sql.Message.Contains(IndiceOrigem, StringComparison.Ordinal))
        {
            // Outro lote da mesma configuração criou a origem entre a leitura e a inserção.
            banco.Entry(origem).State = EntityState.Detached;
            return await LerOrigemAsync(banco, lote, ct)
                ?? throw new InvalidOperationException("Origem duplicada, mas não encontrada.", erro);
        }
    }

    private static Task<OrigemSincronizacao?> LerOrigemAsync(PlataformaDbContext banco, LoteVendas lote, CancellationToken ct) =>
        banco.OrigensSincronizacao.AsNoTracking()
            .SingleOrDefaultAsync(o => o.ConfiguracaoId == lote.ConfiguracaoId && o.Rotina == lote.Rotina, ct);

    /// <summary>
    /// Lê o corpo até <see cref="Limites.BytesPorLote"/>; acima disso → 413. JSON ilegível → 400.
    /// </summary>
    // SPEC_DEVIATION: o design põe o limite de 2 MB no Kestrel da rota; aqui o corpo é lido com limite no endpoint.
    // Reason: o 413 do Kestrel sai com o código REQUISICAO_INVALIDA (não LOTE_GRANDE_DEMAIS) e o TestServer não aplica
    // o limite. O limite global do Kestrel (30 MB) continua protegendo contra corpos maiores.
    private static async Task<(LoteVendas? Lote, IResult? Erro)> LerAsync(HttpRequest requisicao, CancellationToken ct)
    {
        if (requisicao.ContentLength > Limites.BytesPorLote) return (null, LoteGrandeDemais());

        using var corpo = new MemoryStream();
        var buffer = new byte[81920];
        int lidos;
        while ((lidos = await requisicao.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (corpo.Length + lidos > Limites.BytesPorLote) return (null, LoteGrandeDemais());
            corpo.Write(buffer, 0, lidos);
        }

        try
        {
            var lote = JsonSerializer.Deserialize<LoteVendas>(corpo.GetBuffer().AsSpan(0, (int)corpo.Length), JsonSerializerOptions.Web);
            return lote is null ? (null, RequisicaoInvalida()) : (lote, null);
        }
        catch (JsonException)
        {
            return (null, RequisicaoInvalida());
        }
    }

    private static IResult RequisicaoInvalida() =>
        ProblemasApi.Problema(StatusCodes.Status400BadRequest, "REQUISICAO_INVALIDA", "Requisição inválida");

    /// <summary>Campos do lote (não das vendas, que são validadas uma a uma).</summary>
    private static Dictionary<string, string[]> Validar(LoteVendas lote)
    {
        var erros = new Dictionary<string, string[]>();
        if (lote.BatchId == Guid.Empty) erros["batchId"] = ["Informe o identificador do lote."];
        if (lote.ConfiguracaoId == Guid.Empty) erros["configuracaoId"] = ["Informe o identificador da configuração."];
        if (string.IsNullOrWhiteSpace(lote.Rotina) || lote.Rotina.Length > OrigemSincronizacao.RotinaMaxima)
            erros["rotina"] = [$"Informe a rotina com até {OrigemSincronizacao.RotinaMaxima} caracteres."];
        if (lote.Vendas is null) erros["vendas"] = ["Informe as vendas do lote."];
        return erros;
    }

    private static string? Cortar(string? texto, int maximo) =>
        texto is not null && texto.Length > maximo ? texto[..maximo] : texto;
}
