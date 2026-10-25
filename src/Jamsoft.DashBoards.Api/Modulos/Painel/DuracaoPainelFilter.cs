using System.Diagnostics;
using Jamsoft.DashBoards.Api.Seguranca;

namespace Jamsoft.DashBoards.Api.Modulos.Painel;

/// <summary>
/// Loga o tempo de resposta de cada bloco do painel (<c>Painel {Bloco} {DuracaoMs} ms</c>), com o tenant e o tamanho
/// do período, para medir a meta de 2 s. Nunca registra dados de venda (valores, clientes, vendedores, empresas).
/// </summary>
public sealed class DuracaoPainelFilter(ILogger<DuracaoPainelFilter> log, ITenantAtual tenantAtual, TimeProvider tempo)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate next)
    {
        var inicio = Stopwatch.GetTimestamp();
        try
        {
            return await next(contexto);
        }
        finally
        {
            var bloco = contexto.HttpContext.Request.Path.Value?.Split('/').LastOrDefault();
            var duracao = (long)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
            log.LogInformation("Painel {Bloco} {DuracaoMs} ms (tenant {Tenant}, {Dias} dias)",
                bloco, duracao, tenantAtual.IdPublico, Dias(contexto));
        }
    }

    /// <summary>Dias do período pedido (inicio e fim são os 2 primeiros parâmetros das rotas); nulo se inválido.</summary>
    private int? Dias(EndpointFilterInvocationContext contexto) =>
        FiltroPainel.TentarCriar(contexto.GetArgument<DateOnly?>(0), contexto.GetArgument<DateOnly?>(1), null,
            FiltroPainel.HojeEmSaoPaulo(tempo), out var filtro, out _)
            ? filtro.Dias
            : null;
}
