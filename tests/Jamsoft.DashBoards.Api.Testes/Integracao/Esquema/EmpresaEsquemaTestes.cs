using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class EmpresaEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Mesmo_COD_EMPRESA_e_aceito_em_tenants_diferentes_e_recusado_no_mesmo_tenant()
    {
        var tenantA = await Dados.CriarTenantAsync(banco, "Tenant A");
        var tenantB = await Dados.CriarTenantAsync(banco, "Tenant B");

        await using (var contexto = banco.CriarContexto())
        {
            contexto.Empresas.Add(new Empresa { TenantId = tenantA, CodEmpresa = 1, Nome = "Matriz A" });
            contexto.Empresas.Add(new Empresa { TenantId = tenantB, CodEmpresa = 1, Nome = "Matriz B" });
            await contexto.SaveChangesAsync();
        }

        await using var duplicada = banco.CriarContexto();
        duplicada.Empresas.Add(new Empresa { TenantId = tenantA, CodEmpresa = 1, Nome = "Outra A" });
        var erro = await Dados.FalhaSqlAsync(() => duplicada.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_EMPRESA_TENANT_ID_COD_EMPRESA", erro.Message);
    }

    [Fact]
    public async Task Indice_unico_de_EMPRESA_comeca_por_TENANT_ID()
    {
        var primeiraColuna = await banco.ConsultarEscalarAsync<string>("""
            SELECT c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.EMPRESA') AND i.name = 'IX_EMPRESA_TENANT_ID_COD_EMPRESA'
              AND ic.key_ordinal = 1
            """);

        Assert.Equal("TENANT_ID", primeiraColuna);
    }
}
