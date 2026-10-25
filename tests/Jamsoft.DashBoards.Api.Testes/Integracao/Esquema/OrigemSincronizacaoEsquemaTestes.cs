using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

/// <summary>ORIGEM_SINCRONIZACAO e EMPRESA.ORIGEM_ID (ING-09, ING-10).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class OrigemSincronizacaoEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Mesma_configuracao_e_rotina_e_recusada_no_mesmo_tenant_e_aceita_em_outro()
    {
        var tenantA = await Dados.CriarTenantAsync(banco);
        var tenantB = await Dados.CriarTenantAsync(banco);
        var coletor = await Dados.NovoColetorAsync(banco);
        var configuracao = Guid.NewGuid();
        await using (var contexto = banco.CriarContexto())
        {
            contexto.OrigensSincronizacao.Add(new OrigemSincronizacao
            { TenantId = tenantA, ColetorId = coletor.Id, ConfiguracaoId = configuracao, Rotina = "Vendas" });
            contexto.OrigensSincronizacao.Add(new OrigemSincronizacao
            { TenantId = tenantB, ColetorId = coletor.Id, ConfiguracaoId = configuracao, Rotina = "Vendas" });
            await contexto.SaveChangesAsync();
        }

        await using var duplicada = banco.CriarContexto();
        duplicada.OrigensSincronizacao.Add(new OrigemSincronizacao
        { TenantId = tenantA, ColetorId = coletor.Id, ConfiguracaoId = configuracao, Rotina = "Vendas" });
        var erro = await Dados.FalhaSqlAsync(() => duplicada.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_ORIGEM_SINCRONIZACAO_TENANT_ID_CONFIGURACAO_ID_ROTINA", erro.Message);
        Assert.Equal(2, await banco.ConsultarEscalarAsync<int>(
            $"SELECT COUNT(*) FROM ORIGEM_SINCRONIZACAO WHERE CONFIGURACAO_ID = '{configuracao}'"));
    }

    [Fact]
    public async Task Empresa_existente_aceita_ORIGEM_ID_nulo_e_so_aceita_origem_existente()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        // Como as empresas gravadas antes desta feature: sem informar a origem.
        await banco.ExecutarAsync($"INSERT INTO EMPRESA (TENANT_ID, COD_EMPRESA, NOME) VALUES ({tenant}, 1, 'Matriz')");

        Assert.Equal(1, await banco.ConsultarEscalarAsync<int>(
            $"SELECT COUNT(*) FROM EMPRESA WHERE TENANT_ID = {tenant} AND ORIGEM_ID IS NULL"));

        await using var contexto = banco.CriarContexto();
        contexto.Empresas.Add(new() { TenantId = tenant, CodEmpresa = 2, Nome = "Filial", OrigemId = int.MaxValue });
        var erro = await Dados.FalhaSqlAsync(() => contexto.SaveChangesAsync());
        Assert.Equal(Dados.ErroCheck, erro.Number); // 547 também é o erro de FK
        Assert.Contains("FK_EMPRESA_ORIGEM_SINCRONIZACAO_ORIGEM_ID", erro.Message);
    }
}
