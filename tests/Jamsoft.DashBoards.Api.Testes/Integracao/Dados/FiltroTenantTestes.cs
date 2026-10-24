using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;

// Namespace diferente da pasta: "Integracao.Dados" esconderia a classe auxiliar Dados nos outros testes.
namespace Jamsoft.DashBoards.Api.Testes.Integracao.Isolamento;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class FiltroTenantTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(int TenantA, int TenantB)> SemearAsync()
    {
        var tenantA = await Dados.CriarTenantAsync(banco, "Tenant A");
        var tenantB = await Dados.CriarTenantAsync(banco, "Tenant B");
        await using var contexto = banco.CriarContexto();
        contexto.Empresas.AddRange(
                new Empresa { TenantId = tenantA, CodEmpresa = 1, Nome = "A1" },
                new Empresa { TenantId = tenantA, CodEmpresa = 2, Nome = "A2" },
                new Empresa { TenantId = tenantB, CodEmpresa = 1, Nome = "B1" });
        await contexto.SaveChangesAsync();
        return (tenantA, tenantB);
    }

    [Fact]
    public async Task Contexto_do_tenant_A_lista_so_as_empresas_de_A()
    {
        var (tenantA, _) = await SemearAsync();

        await using var contexto = banco.CriarContexto(new TenantFixo(tenantA));
        var empresas = await contexto.Empresas.OrderBy(e => e.CodEmpresa).ToListAsync();

        Assert.Equal(["A1", "A2"], empresas.Select(e => e.Nome));
        Assert.All(empresas, e => Assert.Equal(tenantA, e.TenantId));
    }

    [Fact]
    public async Task Contexto_sem_tenant_nao_lista_nenhuma_empresa()
    {
        await SemearAsync();

        await using var contexto = banco.CriarContexto(TenantSemAcesso.Instancia);

        Assert.Equal(0, await contexto.Empresas.CountAsync());
        Assert.True(await contexto.Empresas.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant]).AnyAsync());
    }
}
