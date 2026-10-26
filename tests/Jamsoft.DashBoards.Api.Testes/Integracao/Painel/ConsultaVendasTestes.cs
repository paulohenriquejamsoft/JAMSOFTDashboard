using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Painel;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Painel;

/// <summary>Base de consulta do painel (DASH-04, DASH-07, DASH-08).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ConsultaVendasTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Contexto no tenant, como numa requisição, com qualquer aviso do EF virando exceção.</summary>
    private PlataformaDbContext Contexto(Tenant tenant)
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(options, banco.ConnectionString);
        options.ConfigureWarnings(w => w.Default(WarningBehavior.Throw));
        return new PlataformaDbContext(options.Options, new TenantFixo(tenant.Id, idPublico: tenant.IdPublico));
    }

    private static FiltroPainel Filtro(Guid[]? empresas = null, string[]? tipos = null, int[]? vendedores = null)
    {
        Assert.True(FiltroPainel.TentarCriar(VendasSemeadas.Inicio, VendasSemeadas.Fim, empresas, tipos, vendedores,
            VendasSemeadas.Fim, out var filtro, out _));
        return filtro;
    }

    [Fact]
    public async Task So_traz_vendas_do_tenant_do_contexto_e_da_faixa_com_limite_superior_exclusivo()
    {
        var a = await Dados.NovoTenantAsync(banco, "Cliente Painel A");
        var b = await Dados.NovoTenantAsync(banco, "Cliente Painel B");
        await VendasSemeadas.SemearAsync(banco, a);
        await VendasSemeadas.SemearAsync(banco, b);
        await using var contexto = Contexto(a);

        var vendas = await new ConsultaVendas(contexto).Do(Filtro(), null)
            .Select(v => new { v.TenantId, v.VendaId }).ToListAsync();

        Assert.All(vendas, v => Assert.Equal(a.Id, v.TenantId));
        // 7 (21/10 00:00 SP) fica fora; 6 (20/10 23:59 SP) e 1 (11/10 10:00) entram; 8 (10/10 23:59) fica fora.
        Assert.Equal(VendasSemeadas.IdsNoPeriodo, vendas.Select(v => v.VendaId).Order());
    }

    [Fact]
    public async Task Filtro_por_empresa_restringe_as_vendas_a_empresa_escolhida()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Painel Empresas");
        var c = await VendasSemeadas.SemearAsync(banco, tenant);
        await using var contexto = Contexto(tenant);
        var consulta = new ConsultaVendas(contexto);
        var filtro = Filtro([c.Filial.IdPublico]);

        var empresaIds = await consulta.ResolverEmpresasAsync(filtro.Empresas);
        var vendas = await consulta.Do(filtro, empresaIds).Select(v => v.VendaId).ToListAsync();

        Assert.Equal([c.Filial.Id], empresaIds!);
        Assert.Equal(VendasSemeadas.IdsFilialNoPeriodo, vendas.Order());
    }

    [Fact]
    public async Task Filtro_por_tipo_de_documento_restringe_as_vendas_aos_tipos_escolhidos()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Painel Tipos");
        await VendasSemeadas.SemearAsync(banco, tenant);
        await using var contexto = Contexto(tenant);

        var vendas = await new ConsultaVendas(contexto).Do(Filtro(tipos: ["NF-e"]), null).Select(v => v.VendaId).ToListAsync();

        Assert.Equal([1, 5], vendas.Order());
    }

    [Fact]
    public async Task Filtro_por_vendedor_restringe_as_vendas_e_deixa_de_fora_as_sem_vendedor()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Painel Vendedores");
        await VendasSemeadas.SemearAsync(banco, tenant);
        await using var contexto = Contexto(tenant);

        var vendas = await new ConsultaVendas(contexto).Do(Filtro(vendedores: [1]), null).Select(v => v.VendaId).ToListAsync();

        // Ana (1): 1, 3 e 5; as vendas 4 e 6 não têm vendedor.
        Assert.Equal([1, 3, 5], vendas.Order());
    }

    [Fact]
    public async Task Empresa_de_outro_tenant_lanca_404_NAO_ENCONTRADO()
    {
        var a = await Dados.NovoTenantAsync(banco, "Cliente Painel A");
        var b = await Dados.NovoTenantAsync(banco, "Cliente Painel B");
        var ca = await VendasSemeadas.PrepararAsync(banco, a);
        var cb = await VendasSemeadas.PrepararAsync(banco, b);
        await using var contexto = Contexto(a);

        var erro = await Assert.ThrowsAsync<ProblemaApiException>(() =>
            new ConsultaVendas(contexto).ResolverEmpresasAsync([ca.Matriz.IdPublico, cb.Filial.IdPublico]));

        Assert.Equal(404, erro.Status);
        Assert.Equal("NAO_ENCONTRADO", erro.Codigo);
    }

    [Fact]
    public async Task Consultas_traduzem_para_SQL_sem_avaliacao_no_cliente_nem_avisos()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Painel SQL");
        var c = await VendasSemeadas.SemearAsync(banco, tenant);
        await using var contexto = Contexto(tenant);
        var consulta = new ConsultaVendas(contexto);
        var filtro = Filtro([c.Matriz.IdPublico, c.Filial.IdPublico]);

        var empresaIds = await consulta.ResolverEmpresasAsync(filtro.Empresas);
        var soma = await consulta.Do(filtro, empresaIds).SumAsync(v => v.Valor);

        // Todas as situações no período: 100 + 50 + 30 + 20 + 200 + 25,50.
        Assert.Equal(425.50m, soma);
    }
}
