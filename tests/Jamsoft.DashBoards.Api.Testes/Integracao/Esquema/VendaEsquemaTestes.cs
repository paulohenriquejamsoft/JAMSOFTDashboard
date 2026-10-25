using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

/// <summary>VENDA (ING-04, contrato da venda; índice do painel-vendas).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class VendaEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Tenant com coletor, origem e uma empresa: o mínimo para gravar vendas.</summary>
    private async Task<(int Tenant, int Empresa, int Origem)> PrepararAsync()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        var coletor = await Dados.NovoColetorAsync(banco);
        await using var contexto = banco.CriarContexto();
        var origem = new OrigemSincronizacao
        { TenantId = tenant, ColetorId = coletor.Id, ConfiguracaoId = Guid.NewGuid(), Rotina = "Vendas" };
        contexto.OrigensSincronizacao.Add(origem);
        await contexto.SaveChangesAsync();
        var empresa = new Empresa { TenantId = tenant, CodEmpresa = 1, Nome = "Matriz", OrigemId = origem.Id };
        contexto.Empresas.Add(empresa);
        await contexto.SaveChangesAsync();
        return (tenant, empresa.Id, origem.Id);
    }

    private static Venda NovaVenda((int Tenant, int Empresa, int Origem) dados, int vendaId = 1,
        string situacao = "Finalizada", decimal valor = 10m) => new()
        {
            TenantId = dados.Tenant,
            EmpresaId = dados.Empresa,
            OrigemId = dados.Origem,
            CodEmpresa = 1,
            VendaId = vendaId,
            TipoDfe = "NFC-e",
            DataHora = new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.FromHours(-3)),
            Valor = valor,
            Situacao = situacao,
            QtdItens = 2,
        };

    private async Task<SqlException> FalhaAoGravarAsync(Venda venda)
    {
        await using var contexto = banco.CriarContexto();
        contexto.Vendas.Add(venda);
        return await Dados.FalhaSqlAsync(() => contexto.SaveChangesAsync());
    }

    [Fact]
    public async Task Chave_de_negocio_duplicada_e_recusada()
    {
        var dados = await PrepararAsync();
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Vendas.Add(NovaVenda(dados, vendaId: 7));
            await contexto.SaveChangesAsync();
        }

        var erro = await FalhaAoGravarAsync(NovaVenda(dados, vendaId: 7, situacao: "Cancelada"));

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_VENDA_TENANT_ID_COD_EMPRESA_VENDA_ID", erro.Message);
    }

    [Fact]
    public async Task Situacao_fora_do_dominio_e_recusada()
    {
        var dados = await PrepararAsync();

        var erro = await FalhaAoGravarAsync(NovaVenda(dados, situacao: "Aberta"));

        Assert.Equal(Dados.ErroCheck, erro.Number);
        Assert.Contains("CK_VENDA_SITUACAO", erro.Message);
    }

    [Fact]
    public async Task Situacao_Contingencia_com_acento_e_aceita()
    {
        var dados = await PrepararAsync();
        var venda = NovaVenda(dados, situacao: "Contingência");
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Vendas.Add(venda);
            await contexto.SaveChangesAsync();
        }

        Assert.Equal("Contingência", await banco.ConsultarEscalarAsync<string>(
            $"SELECT SITUACAO FROM VENDA WHERE ID = {venda.Id}"));
    }

    [Fact]
    public async Task Valor_negativo_e_recusado()
    {
        var dados = await PrepararAsync();

        var erro = await FalhaAoGravarAsync(NovaVenda(dados, valor: -0.01m));

        Assert.Equal(Dados.ErroCheck, erro.Number);
        Assert.Contains("CK_VENDA_VALOR", erro.Message);
    }

    [Fact]
    public async Task Indice_por_data_comeca_por_TENANT_ID_e_cobre_as_colunas_do_painel()
    {
        var chaves = await banco.ConsultarEscalarAsync<string>(ColunasDoIndice(incluidas: false));
        var incluidas = await banco.ConsultarEscalarAsync<string>(ColunasDoIndice(incluidas: true));

        Assert.Equal("TENANT_ID,DATA_HORA", chaves);
        Assert.Equal("COD_CLIENTE,EMPRESA_ID,QTD_ITENS,SITUACAO,TIPO_DFE,VALOR,VENDEDOR_CODIGO", incluidas);
    }

    private static string ColunasDoIndice(bool incluidas) => $"""
        SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY {(incluidas ? "c.name" : "ic.key_ordinal")})
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID('dbo.VENDA') AND i.name = 'IX_VENDA_TENANT_ID_DATA_HORA'
          AND ic.is_included_column = {(incluidas ? 1 : 0)}
        """;
}
