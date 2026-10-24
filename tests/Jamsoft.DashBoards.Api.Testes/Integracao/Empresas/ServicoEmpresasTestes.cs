using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Empresas;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ServicoEmpresasTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Chama o serviço num contexto do tenant, como a ingestão fará.</summary>
    private async Task<Empresa> ObterOuCriarAsync(int tenantId, int codEmpresa, string nome)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenantId));
        return await new ServicoEmpresas(contexto).ObterOuCriarAsync(tenantId, codEmpresa, nome);
    }

    private async Task SemearAsync(int tenantId, int codEmpresa, string nome, bool nomeManual)
    {
        await using var contexto = banco.CriarContexto();
        contexto.Empresas.Add(new Empresa { TenantId = tenantId, CodEmpresa = codEmpresa, Nome = nome, NomeManual = nomeManual });
        await contexto.SaveChangesAsync();
    }

    private async Task<List<Empresa>> GravadasAsync(int tenantId, int codEmpresa)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenantId));
        return await contexto.Empresas.AsNoTracking().Where(e => e.CodEmpresa == codEmpresa).ToListAsync();
    }

    [Fact]
    public async Task Cria_a_empresa_quando_nao_existe_com_nome_automatico()
    {
        var tenant = await Dados.CriarTenantAsync(banco);

        var empresa = await ObterOuCriarAsync(tenant, 7, "Filial Centro");

        Assert.True(empresa.Id > 0);
        Assert.NotEqual(Guid.Empty, empresa.IdPublico);
        var gravada = Assert.Single(await GravadasAsync(tenant, 7));
        Assert.Equal(empresa.Id, gravada.Id);
        Assert.Equal(tenant, gravada.TenantId);
        Assert.Equal("Filial Centro", gravada.Nome);
        Assert.False(gravada.NomeManual);
    }

    [Fact]
    public async Task Reutiliza_a_empresa_que_ja_existe()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        await SemearAsync(tenant, 3, "Loja 3", nomeManual: false);
        var existente = Assert.Single(await GravadasAsync(tenant, 3));

        var empresa = await ObterOuCriarAsync(tenant, 3, "Loja 3");

        Assert.Equal(existente.Id, empresa.Id);
        Assert.Single(await GravadasAsync(tenant, 3));
    }

    [Fact]
    public async Task Atualiza_o_nome_quando_o_nome_e_automatico()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        await SemearAsync(tenant, 4, "Nome antigo", nomeManual: false);

        var empresa = await ObterOuCriarAsync(tenant, 4, "Nome novo do ERP");

        Assert.Equal("Nome novo do ERP", empresa.Nome);
        var gravada = Assert.Single(await GravadasAsync(tenant, 4));
        Assert.Equal("Nome novo do ERP", gravada.Nome);
        Assert.False(gravada.NomeManual);
    }

    [Fact]
    public async Task Preserva_o_nome_editado_manualmente()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        await SemearAsync(tenant, 5, "Nome do Admin", nomeManual: true);

        var empresa = await ObterOuCriarAsync(tenant, 5, "Nome do ERP");

        Assert.Equal("Nome do Admin", empresa.Nome);
        var gravada = Assert.Single(await GravadasAsync(tenant, 5));
        Assert.Equal("Nome do Admin", gravada.Nome);
        Assert.True(gravada.NomeManual);
    }

    [Fact]
    public async Task Chamadas_simultaneas_criam_uma_unica_empresa()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        const int chamadas = 8;
        var prontas = 0;
        var largada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var empresas = await Task.WhenAll(Enumerable.Range(0, chamadas).Select(_ => Task.Run(async () =>
        {
            // Todas partem juntas, cada uma com seu contexto (como requisições diferentes).
            if (Interlocked.Increment(ref prontas) == chamadas) largada.SetResult();
            await largada.Task;
            return await ObterOuCriarAsync(tenant, 9, "Filial disputada");
        })));

        var gravada = Assert.Single(await GravadasAsync(tenant, 9));
        Assert.All(empresas, e => Assert.Equal(gravada.Id, e.Id));
    }
}
