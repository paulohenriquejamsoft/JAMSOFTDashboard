using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;

// Namespace diferente da pasta: "Integracao.Dados" esconderia a classe auxiliar Dados nos outros testes.
namespace Jamsoft.DashBoards.Api.Testes.Integracao.Isolamento;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class TenantSaveChangesInterceptorTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private int _tenantA;
    private int _tenantB;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _tenantA = await Dados.CriarTenantAsync(banco, "Tenant A");
        _tenantB = await Dados.CriarTenantAsync(banco, "Tenant B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PlataformaDbContext ContextoDeA() => banco.CriarContexto(new TenantFixo(_tenantA));

    /// <summary>Lê direto do banco, sem filtro: é o que de fato foi gravado.</summary>
    private async Task<List<Empresa>> EmpresasGravadasAsync(int tenantId)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Empresas.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant]).AsNoTracking()
            .Where(e => e.TenantId == tenantId).OrderBy(e => e.CodEmpresa).ToListAsync();
    }

    private async Task<Empresa> SemearEmpresaDeBAsync()
    {
        await using var contexto = banco.CriarContexto();
        var empresa = new Empresa { TenantId = _tenantB, CodEmpresa = 1, Nome = "B1" };
        contexto.Empresas.Add(empresa);
        await contexto.SaveChangesAsync();
        return empresa;
    }

    [Fact]
    public async Task Insercao_sem_TenantId_recebe_o_tenant_atual()
    {
        await using (var contexto = ContextoDeA())
        {
            contexto.Empresas.Add(new Empresa { CodEmpresa = 7, Nome = "Sem tenant informado" });
            await contexto.SaveChangesAsync();
        }

        var gravada = Assert.Single(await EmpresasGravadasAsync(_tenantA));
        Assert.Equal(7, gravada.CodEmpresa);
        Assert.Equal(_tenantA, gravada.TenantId);
    }

    [Fact]
    public async Task Insercao_de_outro_tenant_lanca_excecao_e_nao_grava_nada()
    {
        await using (var contexto = ContextoDeA())
        {
            contexto.Empresas.Add(new Empresa { TenantId = _tenantA, CodEmpresa = 1, Nome = "A1" });
            contexto.Empresas.Add(new Empresa { TenantId = _tenantB, CodEmpresa = 1, Nome = "B1" });
            await Assert.ThrowsAsync<AcessoCruzadoException>(() => contexto.SaveChangesAsync());
        }

        Assert.Empty(await EmpresasGravadasAsync(_tenantA));
        Assert.Empty(await EmpresasGravadasAsync(_tenantB));
    }

    [Fact]
    public async Task Alteracao_de_empresa_de_outro_tenant_lanca_excecao_e_nao_grava()
    {
        var empresaB = await SemearEmpresaDeBAsync();

        await using (var contexto = ContextoDeA())
        {
            var empresa = await contexto.Empresas.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
                .SingleAsync(e => e.Id == empresaB.Id);
            empresa.Nome = "Alterada por A";
            await Assert.ThrowsAsync<AcessoCruzadoException>(() => contexto.SaveChangesAsync());
        }

        Assert.Equal("B1", Assert.Single(await EmpresasGravadasAsync(_tenantB)).Nome);
    }

    [Fact]
    public async Task Exclusao_de_empresa_de_outro_tenant_lanca_excecao_e_nao_exclui()
    {
        var empresaB = await SemearEmpresaDeBAsync();

        await using (var contexto = ContextoDeA())
        {
            var empresa = await contexto.Empresas.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
                .SingleAsync(e => e.Id == empresaB.Id);
            contexto.Empresas.Remove(empresa);
            await Assert.ThrowsAsync<AcessoCruzadoException>(() => contexto.SaveChangesAsync());
        }

        Assert.Single(await EmpresasGravadasAsync(_tenantB));
    }

    [Fact]
    public async Task Dentro_de_OperacaoGlobal_insercao_alteracao_e_exclusao_de_outro_tenant_gravam()
    {
        var empresaB = await SemearEmpresaDeBAsync();

        await using (var contexto = ContextoDeA())
        using (contexto.OperacaoGlobal())
        {
            var existente = await contexto.Empresas.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
                .SingleAsync(e => e.Id == empresaB.Id);
            existente.Nome = "Alterada globalmente";
            contexto.Empresas.Add(new Empresa { TenantId = _tenantB, CodEmpresa = 2, Nome = "B2" });
            await contexto.SaveChangesAsync();

            Assert.Equal(["Alterada globalmente", "B2"], (await EmpresasGravadasAsync(_tenantB)).Select(e => e.Nome));

            contexto.Empresas.Remove(existente);
            await contexto.SaveChangesAsync();
        }

        Assert.Equal(["B2"], (await EmpresasGravadasAsync(_tenantB)).Select(e => e.Nome));
    }
}
