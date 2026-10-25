using System.Data;
using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Modulos.Ingestao;

/// <summary>
/// Apaga as linhas de LOTE_INGESTAO recebidas há mais que <see cref="LimpezaLotesOptions.Retencao"/> (30 dias), em
/// DELETEs de até <see cref="LimpezaLotesOptions.TamanhoLote"/> linhas até sobrar menos que um lote. Atravessa todos os
/// tenants: a regra é só de idade, e o reenvio de um lote acontece em minutos ou horas, nunca depois de 30 dias.
/// </summary>
public sealed class LimpezaLotes(IServiceScopeFactory escopos, IOptions<LimpezaLotesOptions> opcoes, TimeProvider tempo)
{
    private const string Sql = "DELETE TOP (@lote) FROM [LOTE_INGESTAO] WHERE [RECEBIDO_EM] < @limite";

    /// <summary>Faz uma limpeza completa e devolve quantas linhas apagou.</summary>
    public async Task<int> LimparAsync(CancellationToken ct = default)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var lote = opcoes.Value.TamanhoLote;
        var limite = tempo.GetUtcNow().UtcDateTime - opcoes.Value.Retencao;

        var total = 0;
        int removidas;
        do
        {
            removidas = await banco.Database.ExecuteSqlRawAsync(Sql,
                [
                    new SqlParameter("@lote", SqlDbType.Int) { Value = lote },
                    new SqlParameter("@limite", SqlDbType.DateTime2) { Value = limite },
                ], ct);
            total += removidas;
        } while (removidas == lote);
        return total;
    }
}

/// <summary>Roda a <see cref="LimpezaLotes"/> ao subir e depois a cada <see cref="LimpezaLotesOptions.Intervalo"/>.</summary>
public sealed class LimpezaLotesWorker(
    LimpezaLotes limpeza,
    IOptions<LimpezaLotesOptions> opcoes,
    TimeProvider tempo,
    ILogger<LimpezaLotesWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opcoes.Value.Ativa) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removidas = await limpeza.LimparAsync(stoppingToken);
                if (removidas > 0) log.LogInformation("Limpeza de LOTE_INGESTAO removeu {Linhas} linhas.", removidas);
            }
            catch (Exception erro) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(erro, "Falha na limpeza de LOTE_INGESTAO.");
            }

            try
            {
                await Task.Delay(opcoes.Value.Intervalo, tempo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

public static class IngestaoExtensoes
{
    public static IServiceCollection AddIngestao(this IServiceCollection services)
    {
        services.AddOptions<LimpezaLotesOptions>()
            .BindConfiguration(LimpezaLotesOptions.Secao)
            .Validate(o => o.Valida(), "LimpezaLotes: intervalo e retenção positivos, lote entre 1 e 5000.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LimpezaLotes>();
        services.AddHostedService<LimpezaLotesWorker>();
        return services;
    }
}
