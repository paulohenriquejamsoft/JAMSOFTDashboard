using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Ingestao;

/// <summary>
/// ING-10 (dona da empresa: <c>EMPRESA.ORIGEM_ID</c>), EMP-04 (empresa criada na primeira venda) e ING-07 (uma
/// transação por lote).
/// </summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class EmpresasDoLoteTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/coletor/vendas/lotes";
    /// <summary>Venda que o trigger de teste recusa, para simular uma falha no meio do MERGE.</summary>
    private const int VendaQueFalha = 987654321;

    private ApiFactory _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco);
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private static VendaContrato NovaVenda(int vendaId, int codEmpresa, string nomeEmpresa = "Loja do ERP") => new(
        VendaId: vendaId, TipoDfe: "NF-e", DataHora: new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.FromHours(-3)),
        Valor: 50m, CodCliente: null, NomeCliente: null, VendedorCodigo: null, VendedorNome: null,
        Situacao: "Finalizada", NomeEmpresa: nomeEmpresa, CodEmpresa: codEmpresa, QtdItens: 1);

    private static LoteVendas NovoLote(Tenant tenant, Guid configuracaoId, IReadOnlyList<VendaContrato> vendas) =>
        new(Guid.NewGuid(), tenant.IdPublico, configuracaoId, "Vendas", "SRV/BANCO", vendas);

    private async Task<(Tenant Tenant, string Token)> AutorizadoAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Empresas");
        var (coletor, _) = await Dados.NovoColetorComSegredoAsync(banco, tenant);
        return (tenant, Acesso.TokenColetor(coletor, [tenant.IdPublico]));
    }

    private async Task<HttpResponseMessage> EnviarAsync(string token, LoteVendas lote) =>
        await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token, lote));

    [Fact]
    public async Task Primeira_venda_de_empresa_nova_cria_a_empresa_com_o_nome_recebido_e_a_origem_como_dona()
    {
        var (tenant, token) = await AutorizadoAsync();
        var configuracao = Guid.NewGuid();

        using var resposta = await EnviarAsync(token, NovoLote(tenant, configuracao, [NovaVenda(1, 7, "Filial Sete")]));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        await using var c = banco.CriarContexto(new TenantFixo(tenant.Id));
        var origem = await c.OrigensSincronizacao.SingleAsync(o => o.ConfiguracaoId == configuracao);
        var empresa = await c.Empresas.SingleAsync();
        Assert.Equal((7, "Filial Sete", false, (int?)origem.Id), (empresa.CodEmpresa, empresa.Nome, empresa.NomeManual, empresa.OrigemId));
        var venda = await c.Vendas.SingleAsync();
        Assert.Equal((empresa.Id, origem.Id), (venda.EmpresaId, venda.OrigemId));
    }

    [Fact]
    public async Task Empresa_criada_a_mao_e_adotada_pela_primeira_origem()
    {
        var (tenant, token) = await AutorizadoAsync();
        int manualId;
        await using (var preparo = banco.CriarContexto())
        {
            var manual = new Empresa { TenantId = tenant.Id, CodEmpresa = 3, Nome = "Nome dado pelo Admin", NomeManual = true };
            preparo.Empresas.Add(manual);
            await preparo.SaveChangesAsync();
            manualId = manual.Id;
        }
        var configuracao = Guid.NewGuid();

        using var resposta = await EnviarAsync(token, NovoLote(tenant, configuracao, [NovaVenda(1, 3, "Nome do ERP")]));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        await using var c = banco.CriarContexto(new TenantFixo(tenant.Id));
        var origem = await c.OrigensSincronizacao.SingleAsync(o => o.ConfiguracaoId == configuracao);
        var empresa = await c.Empresas.SingleAsync();
        Assert.Equal((manualId, (int?)origem.Id, "Nome dado pelo Admin"), (empresa.Id, empresa.OrigemId, empresa.Nome));
        Assert.Equal(manualId, (await c.Vendas.SingleAsync()).EmpresaId);
    }

    [Fact]
    public async Task Segunda_configuracao_com_a_mesma_empresa_responde_409_e_nao_grava_nada()
    {
        var (tenant, token) = await AutorizadoAsync();
        using (var primeira = await EnviarAsync(token, NovoLote(tenant, Guid.NewGuid(), [NovaVenda(1, 1)])))
            Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        var antes = await DadosTenant.ImpressaoAsync(banco, tenant.Id);

        // A empresa 2 é nova, mas a 1 já pertence à primeira configuração: o lote inteiro é recusado.
        using var resposta = await EnviarAsync(token, NovoLote(tenant, Guid.NewGuid(), [NovaVenda(1, 2), NovaVenda(2, 1)]));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("EMPRESA_VINCULADA_A_OUTRA_ORIGEM", await Sessao.CodigoAsync(resposta));
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, tenant.Id));
    }

    [Fact]
    public async Task Falha_no_MERGE_desfaz_origem_empresa_e_lote()
    {
        var (tenant, token) = await AutorizadoAsync();
        var antes = await DadosTenant.ImpressaoAsync(banco, tenant.Id);
        await banco.ExecutarAsync($"""
            CREATE TRIGGER TR_TESTE_FALHA_VENDA ON VENDA AFTER INSERT, UPDATE AS
            IF EXISTS (SELECT 1 FROM inserted WHERE VENDA_ID = {VendaQueFalha}) THROW 50000, 'Falha injetada', 1;
            """);
        try
        {
            using var resposta = await EnviarAsync(token,
                NovoLote(tenant, Guid.NewGuid(), [NovaVenda(1, 9), NovaVenda(VendaQueFalha, 9)]));

            Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        }
        finally
        {
            await banco.ExecutarAsync("DROP TRIGGER TR_TESTE_FALHA_VENDA;");
        }

        // Nem a origem, nem a empresa 9, nem o lote, nem a venda 1 ficaram gravados.
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, tenant.Id));
    }
}
