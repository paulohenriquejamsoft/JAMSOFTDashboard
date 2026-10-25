using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Ingestao;

/// <summary>Retenção de 30 dias de LOTE_INGESTAO (design, Risks: crescimento da tabela).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class LimpezaLotesTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private readonly FakeTimeProvider _tempo = new(DateTimeOffset.UtcNow);

    public Task InitializeAsync() => banco.MigrarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Mesmos registros do Program.cs, com o relógio falso e o lote informado.</summary>
    private LimpezaLotes Limpeza(int tamanhoLote, out ServiceProvider servicos)
    {
        var colecao = new ServiceCollection();
        colecao.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        colecao.AddLogging();
        colecao.AddSingleton<TimeProvider>(_tempo);
        colecao.AddDadosPlataforma(banco.ConnectionString);
        colecao.AddIngestao();
        colecao.Configure<LimpezaLotesOptions>(o => o.TamanhoLote = tamanhoLote);
        servicos = colecao.BuildServiceProvider();
        return servicos.GetRequiredService<LimpezaLotes>();
    }

    /// <summary>Grava lotes de uma origem nova, recebidos há <paramref name="dias"/> dias, e devolve os BATCH_ID.</summary>
    private async Task<List<Guid>> LotesAsync(int quantidade, int dias)
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var coletor = await Dados.NovoColetorAsync(banco);
        await using var contexto = banco.CriarContexto();
        var origem = new OrigemSincronizacao
        {
            TenantId = tenant.Id,
            ColetorId = coletor.Id,
            ConfiguracaoId = Guid.NewGuid(),
            Rotina = "Vendas",
        };
        contexto.OrigensSincronizacao.Add(origem);
        await contexto.SaveChangesAsync();
        var lotes = Enumerable.Range(0, quantidade).Select(_ => new LoteIngestao
        {
            TenantId = tenant.Id,
            BatchId = Guid.NewGuid(),
            OrigemId = origem.Id,
            RecebidoEm = _tempo.GetUtcNow().UtcDateTime.AddDays(-dias),
            Resposta = "{}",
        }).ToList();
        contexto.LotesIngestao.AddRange(lotes);
        await contexto.SaveChangesAsync();
        return lotes.Select(l => l.BatchId).ToList();
    }

    private async Task<List<Guid>> RestantesAsync(IEnumerable<Guid> batchIds)
    {
        var ids = batchIds.ToList();
        await using var contexto = banco.CriarContexto();
        return await contexto.LotesIngestao.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
            .Where(l => ids.Contains(l.BatchId)).Select(l => l.BatchId).ToListAsync();
    }

    [Fact]
    public async Task Lotes_de_31_dias_sao_apagados_e_os_de_29_dias_mantidos()
    {
        var antigos = await LotesAsync(2, dias: 31);
        var recentes = await LotesAsync(2, dias: 29);
        var limpeza = Limpeza(LimpezaLotesOptions.LoteMaximo, out var servicos);
        await using var _ = servicos;

        var removidas = await limpeza.LimparAsync();

        Assert.Equal(2, removidas);
        Assert.Empty(await RestantesAsync(antigos));
        Assert.Equal(recentes.Order(), (await RestantesAsync(recentes)).Order());
    }

    [Fact]
    public async Task Varias_passadas_quando_os_antigos_passam_do_tamanho_do_lote()
    {
        // 7 antigos com lote de 3: precisa de 3 DELETEs (3 + 3 + 1).
        var antigos = await LotesAsync(7, dias: 40);
        var recente = await LotesAsync(1, dias: 1);
        var limpeza = Limpeza(3, out var servicos);
        await using var _ = servicos;

        var removidas = await limpeza.LimparAsync();

        Assert.Equal(7, removidas);
        Assert.Empty(await RestantesAsync(antigos));
        Assert.Equal(recente, await RestantesAsync(recente));
    }
}
