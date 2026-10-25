using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

/// <summary>LOTE_INGESTAO (ING-05).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class LoteIngestaoEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(int Tenant, int Origem)> TenantComOrigemAsync(int coletor)
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        await using var contexto = banco.CriarContexto();
        var origem = new OrigemSincronizacao
            { TenantId = tenant, ColetorId = coletor, ConfiguracaoId = Guid.NewGuid(), Rotina = "Vendas" };
        contexto.OrigensSincronizacao.Add(origem);
        await contexto.SaveChangesAsync();
        return (tenant, origem.Id);
    }

    private static LoteIngestao Lote((int Tenant, int Origem) destino, Guid batchId) => new()
        { TenantId = destino.Tenant, OrigemId = destino.Origem, BatchId = batchId, Resposta = "{}" };

    [Fact]
    public async Task Mesmo_BATCH_ID_e_recusado_no_mesmo_tenant_e_aceito_em_outro()
    {
        var coletor = await Dados.NovoColetorAsync(banco);
        var a = await TenantComOrigemAsync(coletor.Id);
        var b = await TenantComOrigemAsync(coletor.Id);
        var batchId = Guid.NewGuid();
        await using (var contexto = banco.CriarContexto())
        {
            contexto.LotesIngestao.Add(Lote(a, batchId));
            contexto.LotesIngestao.Add(Lote(b, batchId));
            await contexto.SaveChangesAsync();
        }

        await using var duplicado = banco.CriarContexto();
        duplicado.LotesIngestao.Add(Lote(a, batchId));
        var erro = await Dados.FalhaSqlAsync(() => duplicado.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_LOTE_INGESTAO_TENANT_ID_BATCH_ID", erro.Message);
        Assert.Equal(2, await banco.ConsultarEscalarAsync<int>(
            $"SELECT COUNT(*) FROM LOTE_INGESTAO WHERE BATCH_ID = '{batchId}'"));
    }
}
