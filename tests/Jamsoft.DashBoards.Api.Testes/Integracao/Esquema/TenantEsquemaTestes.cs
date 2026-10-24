using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class TenantEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migracao_cria_TENANT_com_as_colunas_do_design()
    {
        var colunas = await banco.ConsultarEscalarAsync<string>("""
            SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME)
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TENANT'
            """);

        Assert.Equal("ALTERADO_EM,ATIVO,CNPJ,CRIADO_EM,ID,ID_PUBLICO,NOME,VERSAO", colunas);
    }

    [Fact]
    public async Task CNPJ_duplicado_viola_indice_unico()
    {
        var cnpj = Dados.CnpjQualquer();
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Tenants.Add(new Tenant { Nome = "Loja Centro", Cnpj = cnpj });
            await contexto.SaveChangesAsync();
        }

        await using var outro = banco.CriarContexto();
        outro.Tenants.Add(new Tenant { Nome = "Loja Norte", Cnpj = cnpj });
        var erro = await Dados.FalhaSqlAsync(() => outro.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_TENANT_CNPJ", erro.Message);
    }

    [Fact]
    public async Task ID_PUBLICO_e_gerado_pelo_banco()
    {
        int id;
        await using (var contexto = banco.CriarContexto())
        {
            var tenant = new Tenant { Nome = "Loja Sul", Cnpj = Dados.CnpjQualquer() };
            contexto.Tenants.Add(tenant);
            await contexto.SaveChangesAsync();
            id = tenant.Id;
        }

        await using var leitura = banco.CriarContexto();
        var gravado = await leitura.Tenants.AsNoTracking().SingleAsync(t => t.Id == id);

        Assert.NotEqual(Guid.Empty, gravado.IdPublico);
        Assert.True(gravado.Ativo);
    }
}
