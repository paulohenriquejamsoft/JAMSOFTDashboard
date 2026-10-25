using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Ingestao;

/// <summary>ING-04: upsert por (TENANT_ID, COD_EMPRESA, VENDA_ID) num MERGE por lote (AD-014).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class GravadorVendasTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Tenant com uma origem e a empresa 1 dessa origem.</summary>
    private sealed record Cenario(Tenant Tenant, int OrigemId, Dictionary<int, int> Empresas);

    private async Task<Cenario> CenarioAsync()
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
        var empresa = new Empresa { TenantId = tenant.Id, CodEmpresa = 1, Nome = "Loja", OrigemId = origem.Id };
        contexto.Empresas.Add(empresa);
        await contexto.SaveChangesAsync();
        return new Cenario(tenant, origem.Id, new Dictionary<int, int> { [1] = empresa.Id });
    }

    private static VendaContrato NovaVenda(int vendaId, string situacao = "Finalizada", decimal valor = 10m) => new(
        VendaId: vendaId, TipoDfe: "NFC-e", DataHora: new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.FromHours(-3)),
        Valor: valor, CodCliente: 10, NomeCliente: "Cliente", VendedorCodigo: 3, VendedorNome: "Vendedor",
        Situacao: situacao, NomeEmpresa: "Loja", CodEmpresa: 1, QtdItens: 2);

    private async Task<(int Inseridos, int Atualizados)> GravarAsync(Cenario c, IReadOnlyList<VendaContrato> vendas)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(c.Tenant.Id));
        return await GravadorVendas.GravarAsync(contexto, c.Tenant.Id, c.OrigemId, vendas, c.Empresas);
    }

    private async Task<List<Venda>> VendasAsync(int tenantId)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenantId));
        return await contexto.Vendas.AsNoTracking().OrderBy(v => v.VendaId).ToListAsync();
    }

    [Fact]
    public async Task Lote_novo_insere_todas_as_vendas_com_os_campos_recebidos()
    {
        var c = await CenarioAsync();
        var vendas = Enumerable.Range(1, 5).Select(i => NovaVenda(i, valor: i * 1.5m)).ToList();

        var resultado = await GravarAsync(c, vendas);

        Assert.Equal((5, 0), resultado);
        var gravadas = await VendasAsync(c.Tenant.Id);
        Assert.Equal([1, 2, 3, 4, 5], gravadas.Select(v => v.VendaId));
        var primeira = gravadas[0];
        Assert.Equal((c.Tenant.Id, c.Empresas[1], 1, "NFC-e", vendas[0].DataHora, 1.5m, 10, "Cliente", 3, "Vendedor",
                "Finalizada", 2, c.OrigemId),
            (primeira.TenantId, primeira.EmpresaId, primeira.CodEmpresa, primeira.TipoDfe, primeira.DataHora,
                primeira.Valor, primeira.CodCliente, primeira.NomeCliente, primeira.VendedorCodigo, primeira.VendedorNome,
                primeira.Situacao, primeira.QtdItens, primeira.OrigemId));
        Assert.All(gravadas, v => Assert.Null(v.AlteradoEm));
    }

    [Fact]
    public async Task Mesmo_lote_com_situacao_alterada_atualiza_todas_com_os_campos_novos_e_ALTERADO_EM()
    {
        var c = await CenarioAsync();
        await GravarAsync(c, Enumerable.Range(1, 3).Select(i => NovaVenda(i)).ToList());
        var alteradas = Enumerable.Range(1, 3).Select(i => NovaVenda(i, "Cancelada", 99m) with
        {
            NomeCliente = null,
            VendedorNome = "Outro",
            QtdItens = 7,
            TipoDfe = "NF-e",
        }).ToList();

        var resultado = await GravarAsync(c, alteradas);

        Assert.Equal((0, 3), resultado);
        var gravadas = await VendasAsync(c.Tenant.Id);
        Assert.Equal(3, gravadas.Count);
        Assert.All(gravadas, v =>
        {
            Assert.Equal(("Cancelada", 99m, (string?)null, "Outro", 7, "NF-e"),
                (v.Situacao, v.Valor, v.NomeCliente, v.VendedorNome, v.QtdItens, v.TipoDfe));
            Assert.NotNull(v.AlteradoEm);
        });
    }

    [Fact]
    public async Task Lote_misto_devolve_contagens_exatas()
    {
        var c = await CenarioAsync();
        await GravarAsync(c, Enumerable.Range(1, 4).Select(i => NovaVenda(i)).ToList());

        var resultado = await GravarAsync(c, Enumerable.Range(3, 5).Select(i => NovaVenda(i, "Cancelada")).ToList());

        Assert.Equal((5 - 2, 2), resultado);
        var gravadas = await VendasAsync(c.Tenant.Id);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], gravadas.Select(v => v.VendaId));
        Assert.Equal(["Finalizada", "Finalizada"], gravadas.Take(2).Select(v => v.Situacao));
        Assert.All(gravadas.Skip(2), v => Assert.Equal("Cancelada", v.Situacao));
    }

    [Fact]
    public async Task Dois_lotes_sobrepostos_em_paralelo_nao_duplicam_e_somam_coerente()
    {
        var c = await CenarioAsync();
        var loteA = Enumerable.Range(1, 300).Select(i => NovaVenda(i)).ToList();
        var loteB = Enumerable.Range(151, 300).Select(i => NovaVenda(i, "Cancelada")).ToList();

        var resultados = await Task.WhenAll(
            Task.Run(() => GravarAsync(c, loteA)),
            Task.Run(() => GravarAsync(c, loteB)));

        // 450 chaves distintas: cada uma inserida uma vez; as 150 em comum são atualizadas pelo segundo lote.
        Assert.Equal(450, resultados.Sum(r => r.Inseridos));
        Assert.Equal(150, resultados.Sum(r => r.Atualizados));
        var gravadas = await VendasAsync(c.Tenant.Id);
        Assert.Equal(450, gravadas.Count);
        Assert.Equal(Enumerable.Range(1, 450), gravadas.Select(v => v.VendaId));
    }

    [Fact]
    public async Task Tenant_vem_do_parametro_e_venda_de_outro_tenant_com_a_mesma_chave_nao_e_tocada()
    {
        var a = await CenarioAsync();
        var b = await CenarioAsync();
        await GravarAsync(b, [NovaVenda(1, "Finalizada", 5m)]);
        var antesB = await DadosTenant.ImpressaoAsync(banco, b.Tenant.Id);

        var resultado = await GravarAsync(a, [NovaVenda(1, "Cancelada", 50m)]);

        Assert.Equal((1, 0), resultado);
        Assert.Equal(antesB, await DadosTenant.ImpressaoAsync(banco, b.Tenant.Id));
        var vendaA = Assert.Single(await VendasAsync(a.Tenant.Id));
        Assert.Equal((a.Tenant.Id, "Cancelada", 50m), (vendaA.TenantId, vendaA.Situacao, vendaA.Valor));
    }
}
