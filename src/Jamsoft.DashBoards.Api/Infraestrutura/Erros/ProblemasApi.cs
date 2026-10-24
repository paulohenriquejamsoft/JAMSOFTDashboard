using System.Diagnostics;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Infraestrutura.Erros;

/// <summary>
/// Respostas de erro da API (AD-011): sempre application/problem+json, com <c>title</c> em pt-BR,
/// a extensão <c>codigo</c> (estável, para o frontend e o Coletor) e <c>traceId</c>.
/// </summary>
public static class ProblemasApi
{
    public const string Codigo = "codigo";
    public const string Erros = "erros";
    public const string TraceId = "traceId";

    public const string CodigoValidacao = "VALIDACAO";
    public const string CodigoErroInterno = "ERRO_INTERNO";

    /// <summary>Código padrão quando a resposta de erro não trouxe um específico.</summary>
    private static readonly Dictionary<int, (string Codigo, string Titulo)> Padroes = new()
    {
        [StatusCodes.Status400BadRequest] = ("REQUISICAO_INVALIDA", "Requisição inválida"),
        [StatusCodes.Status401Unauthorized] = ("NAO_AUTENTICADO", "Autenticação necessária"),
        [StatusCodes.Status403Forbidden] = ("ACESSO_NEGADO", "Acesso não permitido"),
        [StatusCodes.Status404NotFound] = ("NAO_ENCONTRADO", "Registro não encontrado"),
        [StatusCodes.Status405MethodNotAllowed] = ("METODO_NAO_PERMITIDO", "Método não permitido"),
        [StatusCodes.Status409Conflict] = ("CONFLITO", "Conflito com o estado atual"),
        [StatusCodes.Status429TooManyRequests] = ("MUITAS_REQUISICOES", "Aguarde um minuto"),
        [StatusCodes.Status500InternalServerError] = (CodigoErroInterno, "Erro inesperado"),
    };

    public static IResult Problema(int status, string codigo, string titulo, IDictionary<string, object?>? extras = null)
    {
        var extensoes = new Dictionary<string, object?>(extras ?? new Dictionary<string, object?>())
        {
            [Codigo] = codigo
        };
        return Results.Problem(statusCode: status, title: titulo, extensions: extensoes);
    }

    public static IResult Validacao(IDictionary<string, string[]> erros) =>
        Problema(StatusCodes.Status400BadRequest, CodigoValidacao, "Dados inválidos",
            new Dictionary<string, object?> { [Erros] = erros });

    /// <summary>Registra ProblemDetails, o tratador de exceções e os códigos padrão por status.</summary>
    public static IServiceCollection AddErrosApi(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = Completar);
        services.AddExceptionHandler<TratadorExcecoes>();
        return services;
    }

    /// <summary>Exceções e respostas de erro sem corpo viram ProblemDetails.</summary>
    public static IApplicationBuilder UseErrosApi(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    private static void Completar(ProblemDetailsContext contexto)
    {
        var problema = contexto.ProblemDetails;
        var status = problema.Status ?? contexto.HttpContext.Response.StatusCode;

        // Sem código específico: usa o código e o título em pt-BR padrão do status.
        if (!problema.Extensions.ContainsKey(Codigo) && Padroes.TryGetValue(status, out var padrao))
        {
            problema.Extensions[Codigo] = padrao.Codigo;
            problema.Title = padrao.Titulo;
        }

        problema.Extensions[TraceId] = Activity.Current?.Id ?? contexto.HttpContext.TraceIdentifier;
        // Nunca expor detalhes internos (mensagem de exceção, stack) no corpo.
        if (status >= StatusCodes.Status500InternalServerError) problema.Detail = null;
        problema.Extensions.Remove("exception");
    }
}

/// <summary>Erro de regra que deve chegar ao cliente com status e código próprios.</summary>
public sealed class ProblemaApiException(int status, string codigo, string titulo) : Exception(titulo)
{
    public int Status { get; } = status;
    public string Codigo { get; } = codigo;
    public string Titulo { get; } = titulo;
}

/// <summary>
/// Converte exceções em ProblemDetails: <see cref="ProblemaApiException"/> mantém status e código;
/// corpo de requisição ilegível vira 400; escrita fora do tenant (<see cref="AcessoCruzadoException"/>) vira
/// 403 ACESSO_NEGADO com registro ACESSO_CRUZADO na auditoria; o resto vira 500 ERRO_INTERNO, registrado no log.
/// </summary>
public sealed class TratadorExcecoes(IProblemDetailsService problemas, ILogger<TratadorExcecoes> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception excecao, CancellationToken ct)
    {
        var (status, codigo, titulo) = excecao switch
        {
            ProblemaApiException p => (p.Status, p.Codigo, p.Titulo),
            BadHttpRequestException b => (b.StatusCode, "REQUISICAO_INVALIDA", "Requisição inválida"),
            AcessoCruzadoException => (StatusCodes.Status403Forbidden, "ACESSO_NEGADO", "Acesso não permitido"),
            _ => (StatusCodes.Status500InternalServerError, ProblemasApi.CodigoErroInterno, "Erro inesperado"),
        };

        if (status >= StatusCodes.Status500InternalServerError)
            log.LogError(excecao, "Erro não tratado em {Metodo} {Rota}.", contexto.Request.Method, contexto.Request.Path);

        if (excecao is AcessoCruzadoException cruzado)
            await AuditarAcessoCruzadoAsync(contexto, cruzado, ct);

        contexto.Response.StatusCode = status;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            Exception = excecao,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = titulo,
                Extensions = { [ProblemasApi.Codigo] = codigo }
            }
        });
    }

    /// <summary>
    /// Registra a tentativa (ISO-02/ISO-04) com o tenant afetado. Falha ao auditar vai para o log e não
    /// muda a resposta 403.
    /// </summary>
    private async Task AuditarAcessoCruzadoAsync(HttpContext contexto, AcessoCruzadoException excecao, CancellationToken ct)
    {
        try
        {
            var servicos = contexto.RequestServices;
            var banco = servicos.GetRequiredService<PlataformaDbContext>();
            var afetado = await banco.Tenants.AsNoTracking()
                .Where(t => t.Id == excecao.TenantDaEntidade)
                .Select(t => new { t.IdPublico, t.Nome })
                .SingleOrDefaultAsync(ct);

            await servicos.GetRequiredService<IAuditoria>().RegistrarAgoraAsync(OperacaoAuditada.AcessoCruzado,
                recursoIdPublico: null, afetado?.IdPublico, afetado?.Nome, ResultadoAuditoria.Negado,
                new { metodo = contexto.Request.Method, rota = contexto.Request.Path.Value, entidade = excecao.Entidade }, ct);
        }
        catch (Exception erro)
        {
            log.LogError(erro, "Falha ao auditar acesso cruzado em {Metodo} {Rota}.", contexto.Request.Method, contexto.Request.Path);
        }
    }
}
