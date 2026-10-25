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
/// pelo <see cref="GravadorVendas"/>. O lote é gravado numa transação junto com a linha de <see cref="LoteIngestao"/> e
/// a resposta; um lote já processado (mesmo <c>batchId</c> no tenant) recebe a resposta gravada (ING-05, AD-014).
/// Empresa que já pertence a outra origem → 409 <c>EMPRESA_VINCULADA_A_OUTRA_ORIGEM</c> sem gravar nada (ING-07, ING-10).
/// </summary>
public static class LotesEndpoint
{
    public const string Rota = "/api/coletor/vendas/lotes";

    public const string CodigoLoteGrandeDemais = "LOTE_GRANDE_DEMAIS";
    public const string CodigoTenantNaoAutorizado = "TENANT_NAO_AUTORIZADO";
    public const string CodigoTenantInativo = "TENANT_INATIVO";
    public const string CodigoEmpresaDeOutraOrigem = "EMPRESA_VINCULADA_A_OUTRA_ORIGEM";

    private const string IndiceOrigem = "IX_ORIGEM_SINCRONIZACAO_TENANT_ID_CONFIGURACAO_ID_ROTINA";
    private const string IndiceLote = "IX_LOTE_INGESTAO_TENANT_ID_BATCH_ID";

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

        var (tenantId, recusa) = await AutorizarTenantAsync(lote.Tenant, atual, status, resolvedor, ct);
        if (recusa is not null) return recusa;

        await using var banco = new PlataformaDbContext(opcoes, new TenantDoLote(tenantId, lote.Tenant, atual.UsuarioIdPublico));
        // Lote já processado: a resposta gravada, sem regravar nada (ING-05).
        if (await RespostaGravadaAsync(banco, lote.BatchId, ct) is { } gravada) return Json(gravada);

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var (resultado, origemId) = await GravarAsync(banco, tenantId, atual.UsuarioIdPublico, lote, ct);
        var resposta = JsonSerializer.Serialize(resultado, JsonSerializerOptions.Web);
        var registro = new LoteIngestao
        {
            TenantId = tenantId,
            BatchId = lote.BatchId,
            OrigemId = origemId,
            Inseridos = resultado.Inseridos,
            Atualizados = resultado.Atualizados,
            Rejeitados = resultado.Rejeitados.Count,
            Resposta = resposta,
        };
        banco.LotesIngestao.Add(registro);
        try
        {
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException duplicado) when (Duplicado(duplicado, IndiceLote))
        {
            // O mesmo lote, enviado ao mesmo tempo, foi gravado primeiro: desfaz este e devolve a resposta daquele.
            await transacao.RollbackAsync(ct);
            banco.ChangeTracker.Clear();
            return Json(await RespostaGravadaAsync(banco, lote.BatchId, ct)
                ?? throw new InvalidOperationException("Lote duplicado, mas não encontrado.", duplicado));
        }
        return Json(resposta);
    }

    /// <summary>
    /// O tenant (ID público) está na claim <c>tenants</c> do token (ING-02) e ativo (ING-03); devolve o ID interno ou a
    /// recusa 403.
    /// </summary>
    internal static async Task<(int TenantId, IResult? Recusa)> AutorizarTenantAsync(Guid tenant, ITenantAtual atual,
        StatusAcessoCache status, IResolvedorTenant resolvedor, CancellationToken ct)
    {
        if (!atual.TenantsAutorizadosColetor.Contains(tenant)) return (0, TenantNaoAutorizado());
        if (!await status.TenantAtivoAsync(tenant, ct) || resolvedor.ObterId(tenant) is not { } tenantId)
            return (0, TenantInativo());
        return (tenantId, null);
    }

    private static Task<string?> RespostaGravadaAsync(PlataformaDbContext banco, Guid batchId, CancellationToken ct) =>
        banco.LotesIngestao.AsNoTracking().Where(l => l.BatchId == batchId).Select(l => l.Resposta).SingleOrDefaultAsync(ct);

    /// <summary>A resposta como foi gravada, para o reenvio receber exatamente o mesmo corpo.</summary>
    private static IResult Json(string resposta) => Results.Content(resposta, "application/json");

    private static bool Duplicado(DbUpdateException erro, string indice) =>
        erro.InnerException is SqlException { Number: 2601 or 2627 } sql && sql.Message.Contains(indice, StringComparison.Ordinal);

    private static async Task<(ResultadoLote Resultado, int OrigemId)> GravarAsync(PlataformaDbContext banco, int tenantId,
        Guid? coletorIdPublico, LoteVendas lote, CancellationToken ct)
    {
        var validacao = ValidacaoVenda.Validar(lote.Vendas);
        var origem = await OrigemAsync(banco, tenantId, coletorIdPublico, lote.ConfiguracaoId, lote.Rotina, lote.BancoOrigem, ct);

        var empresas = new Dictionary<int, int>();
        var servico = new ServicoEmpresas(banco);
        foreach (var grupo in validacao.Validas.GroupBy(v => v.CodEmpresa))
        {
            var empresa = await servico.ObterOuCriarAsync(tenantId, grupo.Key, grupo.First().NomeEmpresa, ct);
            // A exceção desfaz a transação do lote inteiro: nada é gravado (ING-10).
            if (!await DaOrigemAsync(banco, empresa, origem.Id, ct))
                throw new ProblemaApiException(StatusCodes.Status409Conflict, CodigoEmpresaDeOutraOrigem,
                    $"Empresa {grupo.Key} vinculada a outra origem");
            empresas[grupo.Key] = empresa.Id;
        }

        var (inseridos, atualizados) = await GravadorVendas.GravarAsync(banco, tenantId, origem.Id, validacao.Validas, empresas, ct);
        return (new ResultadoLote(lote.BatchId, inseridos, atualizados, validacao.Rejeitadas), origem.Id);
    }

    /// <summary>
    /// A empresa pertence à origem do lote (ING-10). Empresa sem dona (nova ou criada à mão) passa a ser desta origem; o
    /// <c>UPDATE</c> condicionado a <c>ORIGEM_ID IS NULL</c> resolve a corrida entre duas origens.
    /// </summary>
    private static async Task<bool> DaOrigemAsync(PlataformaDbContext banco, Empresa empresa, int origemId, CancellationToken ct)
    {
        if (empresa.OrigemId is { } dona) return dona == origemId;
        var adotada = await banco.Empresas.Where(e => e.Id == empresa.Id && e.OrigemId == null)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.OrigemId, origemId), ct);
        return adotada == 1
            || await banco.Empresas.Where(e => e.Id == empresa.Id).Select(e => e.OrigemId).SingleAsync(ct) == origemId;
    }

    /// <summary>
    /// A origem (tenant, configuração, rotina), criada no primeiro lote ou relatório de execução. O contexto precisa
    /// estar no tenant <paramref name="tenantId"/> (<see cref="TenantDoLote"/>).
    /// </summary>
    internal static async Task<OrigemSincronizacao> OrigemAsync(PlataformaDbContext banco, int tenantId,
        Guid? coletorIdPublico, Guid configuracaoId, string rotina, string? bancoOrigem, CancellationToken ct)
    {
        var existente = await LerOrigemAsync(banco, configuracaoId, rotina, ct);
        if (existente is not null) return existente;

        var coletorId = await banco.Coletores.Where(c => c.IdPublico == coletorIdPublico).Select(c => c.Id).SingleAsync(ct);
        var origem = new OrigemSincronizacao
        {
            TenantId = tenantId,
            ColetorId = coletorId,
            ConfiguracaoId = configuracaoId,
            Rotina = rotina,
            BancoOrigem = Cortar(bancoOrigem, OrigemSincronizacao.BancoOrigemMaximo),
        };
        banco.OrigensSincronizacao.Add(origem);
        try
        {
            await banco.SaveChangesAsync(ct);
            return origem;
        }
        catch (DbUpdateException erro) when (Duplicado(erro, IndiceOrigem))
        {
            // Outro lote da mesma configuração criou a origem entre a leitura e a inserção.
            banco.Entry(origem).State = EntityState.Detached;
            return await LerOrigemAsync(banco, configuracaoId, rotina, ct)
                ?? throw new InvalidOperationException("Origem duplicada, mas não encontrada.", erro);
        }
    }

    private static Task<OrigemSincronizacao?> LerOrigemAsync(PlataformaDbContext banco, Guid configuracaoId, string rotina,
        CancellationToken ct) =>
        banco.OrigensSincronizacao.AsNoTracking()
            .SingleOrDefaultAsync(o => o.ConfiguracaoId == configuracaoId && o.Rotina == rotina, ct);

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

    internal static string? Cortar(string? texto, int maximo) =>
        texto is not null && texto.Length > maximo ? texto[..maximo] : texto;
}
