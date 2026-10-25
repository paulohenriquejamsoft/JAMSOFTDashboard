using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>
/// <c>POST /api/coletor/execucoes</c> (policy <see cref="Policies.Coletor"/>; ING-09): o Coletor reporta o fim de uma
/// execução e a <see cref="OrigemSincronizacao"/> da configuração guarda início, fim, situação, registros enviados e a
/// mensagem resumida (até 500 caracteres). <c>ULTIMO_SUCESSO_EM</c> só muda numa execução com Sucesso. Mesmas
/// recusas de tenant da ingestão de lotes (403 <c>TENANT_NAO_AUTORIZADO</c> / <c>TENANT_INATIVO</c>).
/// </summary>
public static class ExecucoesEndpoint
{
    public const string Rota = "/api/coletor/execucoes";
    public const string Sucesso = "Sucesso";

    /// <summary>Resultados de uma execução (LOG-02).</summary>
    public static readonly IReadOnlyList<string> Situacoes = [Sucesso, "Falha", "Cancelada"];

    public static IEndpointRouteBuilder MapExecucoes(this IEndpointRouteBuilder app)
    {
        app.MapPost(Rota, RegistrarAsync).RequireAuthorization(Policies.Coletor);
        return app;
    }

    public static async Task<IResult> RegistrarAsync(RelatorioExecucao corpo, ITenantAtual atual, StatusAcessoCache status,
        IResolvedorTenant resolvedor, DbContextOptions<PlataformaDbContext> opcoes, CancellationToken ct)
    {
        var erros = Validar(corpo);
        if (erros.Count > 0) return ProblemasApi.Validacao(erros);

        var (tenantId, recusa) = await LotesEndpoint.AutorizarTenantAsync(corpo.Tenant, atual, status, resolvedor, ct);
        if (recusa is not null) return recusa;

        await using var banco = new PlataformaDbContext(opcoes, new TenantDoLote(tenantId, corpo.Tenant, atual.UsuarioIdPublico));
        var origem = await LotesEndpoint.OrigemAsync(banco, tenantId, atual.UsuarioIdPublico, corpo.ConfiguracaoId,
            corpo.Rotina, corpo.BancoOrigem, ct);
        // Rastreada: a recém-criada já está no contexto; a existente é carregada agora.
        var registro = await banco.OrigensSincronizacao.SingleAsync(o => o.Id == origem.Id, ct);
        registro.BancoOrigem = LotesEndpoint.Cortar(corpo.BancoOrigem, OrigemSincronizacao.BancoOrigemMaximo);
        registro.UltimaExecucaoInicio = corpo.Inicio.UtcDateTime;
        registro.UltimaExecucaoFim = corpo.Fim.UtcDateTime;
        registro.UltimaSituacao = corpo.Situacao;
        registro.RegistrosUltima = corpo.RegistrosEnviados;
        registro.UltimaMensagem = LotesEndpoint.Cortar(corpo.Mensagem, OrigemSincronizacao.MensagemMaxima);
        if (corpo.Situacao == Sucesso) registro.UltimoSucessoEm = corpo.Fim.UtcDateTime;
        await banco.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> Validar(RelatorioExecucao corpo)
    {
        var erros = new Dictionary<string, string[]>();
        if (corpo.ConfiguracaoId == Guid.Empty) erros["configuracaoId"] = ["Informe o identificador da configuração."];
        if (string.IsNullOrWhiteSpace(corpo.Rotina) || corpo.Rotina.Length > OrigemSincronizacao.RotinaMaxima)
            erros["rotina"] = [$"Informe a rotina com até {OrigemSincronizacao.RotinaMaxima} caracteres."];
        if (!Situacoes.Contains(corpo.Situacao, StringComparer.Ordinal))
            erros["situacao"] = [$"A situação deve ser {string.Join(", ", Situacoes)}."];
        if (corpo.Inicio == default || corpo.Fim == default || corpo.Fim < corpo.Inicio)
            erros["fim"] = ["Informe início e fim, com o fim depois do início."];
        if (corpo.RegistrosEnviados < 0) erros["registrosEnviados"] = ["Os registros enviados não podem ser negativos."];
        return erros;
    }
}
