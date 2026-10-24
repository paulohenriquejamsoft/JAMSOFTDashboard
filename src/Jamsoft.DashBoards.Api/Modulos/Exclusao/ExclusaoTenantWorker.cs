using System.Data;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Modulos.Exclusao;

/// <summary>
/// Executa um job de exclusão de tenant (DEL-04..DEL-08). Reivindica o próximo job (<c>Pendente</c>, ou
/// <c>EmExecucao</c> sem heartbeat recente) com <c>UPDLOCK, READPAST</c>, para dois processos nunca pegarem o mesmo.
/// Para cada etapa do <see cref="PlanoExclusao"/>, a partir de <c>ETAPA_ATUAL</c>, apaga em lotes de até
/// <see cref="ExclusaoOptions.TamanhoLote"/> linhas até um lote afetar 0 linhas. Cada lote é uma transação que também
/// grava etapa, linhas removidas e heartbeat: a retomada recomeça do último lote confirmado. A última etapa remove a
/// linha de TENANT. Exceção → <c>Falhou</c> com a etapa e um erro resumido; o tenant segue inativo.
/// </summary>
public sealed class ProcessadorExclusao(
    IServiceScopeFactory escopos,
    PlanoExclusao plano,
    IOptions<ExclusaoOptions> opcoes,
    TimeProvider tempo,
    ILogger<ProcessadorExclusao> log)
{
    private const int TamanhoMaximoErro = 500;

    /// <summary>Processa o próximo job disponível até o fim. Falso quando não havia job a reivindicar.</summary>
    public async Task<bool> ProcessarProximoAsync(CancellationToken ct = default)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var auditoria = escopo.ServiceProvider.GetRequiredService<IAuditoria>();

        var job = await ReivindicarAsync(banco, auditoria, ct);
        if (job is null) return false;

        var etapa = job.EtapaAtual;
        try
        {
            var linhas = LerLinhas(job.LinhasRemovidas);
            var inicio = Math.Max(0, plano.Etapas.ToList().FindIndex(e => e.Tabela == job.EtapaAtual));
            foreach (var atual in plano.Etapas.Skip(inicio))
            {
                etapa = atual.Tabela;
                await ExecutarEtapaAsync(banco, job, atual, linhas, ct);
            }

            job.Situacao = SituacaoExclusao.Concluida;
            job.ConcluidoEm = job.HeartbeatEm = Agora();
            Auditar(auditoria, job, OperacaoAuditada.ExclusaoConcluida, ResultadoAuditoria.Sucesso);
            await banco.SaveChangesAsync(ct);
        }
        catch (Exception erro) when (!ct.IsCancellationRequested)
        {
            // Cancelado (API parando): o job fica EmExecucao e é retomado quando o heartbeat expirar.
            log.LogError(erro, "Exclusão do tenant {TenantIdPublico} falhou na etapa {Etapa}.", job.TenantIdPublico, etapa);
            await MarcarFalhaAsync(job.Id, etapa, erro, ct);
        }
        return true;
    }

    private async Task<ExclusaoTenant?> ReivindicarAsync(PlataformaDbContext banco, IAuditoria auditoria, CancellationToken ct)
    {
        var agora = Agora();
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var ids = await banco.Database.SqlQueryRaw<int>(
            """
            UPDATE TOP (1) [EXCLUSAO_TENANT] WITH (UPDLOCK, READPAST)
            SET [SITUACAO] = 'EmExecucao', [INICIADO_EM] = COALESCE([INICIADO_EM], @agora), [HEARTBEAT_EM] = @agora
            OUTPUT inserted.[ID] AS [Value]
            WHERE [SITUACAO] = 'Pendente'
               OR ([SITUACAO] = 'EmExecucao' AND ([HEARTBEAT_EM] IS NULL OR [HEARTBEAT_EM] < @limite))
            """,
            new SqlParameter("@agora", SqlDbType.DateTime2) { Value = agora },
            new SqlParameter("@limite", SqlDbType.DateTime2) { Value = agora - opcoes.Value.HeartbeatExpiraEm }).ToListAsync(ct);
        if (ids.Count == 0) return null;

        var job = await banco.ExclusoesTenant.SingleAsync(j => j.Id == ids[0], ct);
        Auditar(auditoria, job, OperacaoAuditada.ExclusaoIniciada, ResultadoAuditoria.Sucesso);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return job;
    }

    private async Task ExecutarEtapaAsync(PlataformaDbContext banco, ExclusaoTenant job, EtapaExclusao etapa,
        Dictionary<string, long> linhas, CancellationToken ct)
    {
        var lote = opcoes.Value.TamanhoLote;
        // Tabela e coluna vêm só do PlanoExclusao (metadados do EF), nunca de entrada externa (DEL-07).
        var sql = $"DELETE TOP ({lote}) FROM [{etapa.Tabela}] WHERE [{etapa.ColunaTenant}] = @tenant";
        while (true)
        {
            int removidas;
            await using (var transacao = await banco.Database.BeginTransactionAsync(ct))
            {
                removidas = await banco.Database.ExecuteSqlRawAsync(sql, [new SqlParameter("@tenant", job.TenantId)], ct);
                linhas[etapa.Tabela] = linhas.GetValueOrDefault(etapa.Tabela) + removidas;
                job.EtapaAtual = etapa.Tabela;
                job.LinhasRemovidas = JsonSerializer.Serialize(linhas);
                job.HeartbeatEm = Agora();
                await banco.SaveChangesAsync(ct);
                await transacao.CommitAsync(ct);
            }
            if (removidas == 0) return;
            await Task.Delay(opcoes.Value.PausaEntreLotes, tempo, ct);
        }
    }

    /// <summary>Em contexto novo: o da execução pode ter ficado com alterações pendentes do lote que falhou.</summary>
    private async Task MarcarFalhaAsync(int jobId, string? etapa, Exception erro, CancellationToken ct)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var banco = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var job = await banco.ExclusoesTenant.SingleAsync(j => j.Id == jobId, ct);
        job.Situacao = SituacaoExclusao.Falhou;
        job.EtapaAtual = etapa;
        job.Erro = Resumir(etapa, erro);
        job.HeartbeatEm = Agora();
        Auditar(escopo.ServiceProvider.GetRequiredService<IAuditoria>(), job, OperacaoAuditada.ExclusaoFalhou,
            ResultadoAuditoria.Falha);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Autor: o sistema (sem usuário). O SuperAdmin solicitante vai nos detalhes (DEL-08).</summary>
    private static void Auditar(IAuditoria auditoria, ExclusaoTenant job, OperacaoAuditada operacao, string resultado) =>
        auditoria.Registrar(operacao, job.IdPublico, job.TenantIdPublico, job.TenantNome, resultado, new
        {
            situacao = job.Situacao.ToString(),
            etapa = job.EtapaAtual,
            solicitadoPorIdPublico = job.SolicitadoPorIdPublico,
        });

    /// <summary>Tipo, número do erro SQL e a primeira linha da mensagem; sem stack nem dados das linhas.</summary>
    private static string Resumir(string? etapa, Exception erro)
    {
        var causa = erro.GetBaseException();
        var codigo = causa is SqlException sql ? $" {sql.Number}" : "";
        var mensagem = causa.Message.Split('\n')[0].Trim();
        var resumo = $"Etapa {etapa ?? "-"}: {causa.GetType().Name}{codigo}: {mensagem}";
        return resumo.Length <= TamanhoMaximoErro ? resumo : resumo[..TamanhoMaximoErro];
    }

    private static Dictionary<string, long> LerLinhas(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? [];

    private DateTime Agora() => tempo.GetUtcNow().UtcDateTime;
}

/// <summary>Busca e processa jobs de exclusão a cada <see cref="ExclusaoOptions.Intervalo"/> enquanto houver.</summary>
public sealed class ExclusaoTenantWorker(
    ProcessadorExclusao processador,
    IOptions<ExclusaoOptions> opcoes,
    TimeProvider tempo,
    ILogger<ExclusaoTenantWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opcoes.Value.WorkerAtivo) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Com job processado, busca o próximo sem esperar.
                if (await processador.ProcessarProximoAsync(stoppingToken)) continue;
            }
            catch (Exception erro) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(erro, "Falha ao buscar job de exclusão de tenant.");
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

public static class ExclusaoExtensoes
{
    public static IServiceCollection AddExclusao(this IServiceCollection services)
    {
        services.AddOptions<ExclusaoOptions>()
            .BindConfiguration(ExclusaoOptions.Secao)
            .Validate(o => o.Valida(), "Exclusao: intervalo e heartbeat positivos, lote entre 1 e 5000, pausa não negativa.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        // Calculado uma vez, a partir do modelo do DbContext.
        services.AddSingleton(sp =>
        {
            using var escopo = sp.CreateScope();
            return PlanoExclusao.De(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().Model);
        });
        services.AddSingleton<ProcessadorExclusao>();
        services.AddHostedService<ExclusaoTenantWorker>();
        return services;
    }
}
