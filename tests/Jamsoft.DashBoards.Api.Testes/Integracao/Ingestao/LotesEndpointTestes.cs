using System.Net;
using System.Text;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Ingestao;

/// <summary><c>POST /api/coletor/vendas/lotes</c>: ING-01, ING-02, ING-03, ING-06, ING-08.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class LotesEndpointTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/coletor/vendas/lotes";

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

    private static VendaContrato NovaVenda(int vendaId, int codEmpresa = 1, string? nomeCliente = "Cliente") => new(
        VendaId: vendaId, TipoDfe: "NFC-e", DataHora: new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.FromHours(-3)),
        Valor: 10m * vendaId, CodCliente: 10, NomeCliente: nomeCliente, VendedorCodigo: 3, VendedorNome: "Vendedor",
        Situacao: "Finalizada", NomeEmpresa: $"Loja {codEmpresa}", CodEmpresa: codEmpresa, QtdItens: 2);

    private static LoteVendas NovoLote(Tenant tenant, IReadOnlyList<VendaContrato> vendas) =>
        new(Guid.NewGuid(), tenant.IdPublico, Guid.NewGuid(), "Vendas", "SRV01/SGE_LOJA", vendas);

    /// <summary>Tenant ativo e token de um coletor autorizado a ele.</summary>
    private async Task<(Tenant Tenant, string Token)> AutorizadoAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Ingestão");
        var (coletor, _) = await Dados.NovoColetorComSegredoAsync(banco, tenant);
        return (tenant, Acesso.TokenColetor(coletor, [tenant.IdPublico]));
    }

    private Task<HttpResponseMessage> EnviarAsync(string token, LoteVendas lote) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token, lote));

    private async Task<int> VendasNoBancoAsync(Tenant tenant)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        return await contexto.Vendas.CountAsync();
    }

    [Fact]
    public async Task Lote_valido_responde_200_com_as_contagens_e_grava_as_vendas()
    {
        var (tenant, token) = await AutorizadoAsync();
        var lote = NovoLote(tenant, [NovaVenda(1), NovaVenda(2), NovaVenda(3, codEmpresa: 2)]);

        using var resposta = await EnviarAsync(token, lote);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        Assert.Equal(lote.BatchId, json.GetProperty("batchId").GetGuid());
        Assert.Equal(3, json.GetProperty("inseridos").GetInt32());
        Assert.Equal(0, json.GetProperty("atualizados").GetInt32());
        Assert.Equal(0, json.GetProperty("rejeitados").GetArrayLength());
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        Assert.Equal([(1, 1, 10m), (1, 2, 20m), (2, 3, 30m)],
            await contexto.Vendas.OrderBy(v => v.VendaId).Select(v => ValueTuple.Create(v.CodEmpresa, v.VendaId, v.Valor)).ToListAsync());
        var origem = await contexto.OrigensSincronizacao.SingleAsync();
        Assert.Equal((lote.ConfiguracaoId, "Vendas", "SRV01/SGE_LOJA"), (origem.ConfiguracaoId, origem.Rotina, origem.BancoOrigem));
        Assert.Equal([1, 2], await contexto.Empresas.OrderBy(e => e.CodEmpresa).Select(e => e.CodEmpresa).ToListAsync());
    }

    [Fact]
    public async Task Uma_venda_invalida_entre_10_grava_9_e_rejeita_1_com_motivo()
    {
        var (tenant, token) = await AutorizadoAsync();
        var vendas = Enumerable.Range(1, 10).Select(i => NovaVenda(i)).ToList();
        vendas[6] = vendas[6] with { Situacao = "Aberta" };

        using var resposta = await EnviarAsync(token, NovoLote(tenant, vendas));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        Assert.Equal(9, json.GetProperty("inseridos").GetInt32());
        var rejeitada = Assert.Single(json.GetProperty("rejeitados").EnumerateArray());
        Assert.Equal(7, rejeitada.GetProperty("vendaId").GetInt32());
        Assert.Equal(1, rejeitada.GetProperty("codEmpresa").GetInt32());
        Assert.Equal("situacao deve ser Finalizada, Cancelada, Inutilizada, Contingência", rejeitada.GetProperty("motivo").GetString());
        Assert.Equal(9, await VendasNoBancoAsync(tenant));
    }

    [Fact]
    public async Task Tenant_fora_do_token_responde_403_TENANT_NAO_AUTORIZADO_sem_gravar()
    {
        var (_, token) = await AutorizadoAsync();
        var outro = await Dados.NovoTenantAsync(banco, "Cliente não autorizado");
        var antes = await DadosTenant.ImpressaoAsync(banco, outro.Id);

        using var resposta = await EnviarAsync(token, NovoLote(outro, [NovaVenda(1)]));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("TENANT_NAO_AUTORIZADO", await Sessao.CodigoAsync(resposta));
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, outro.Id));
    }

    [Fact]
    public async Task Tenant_inativo_responde_403_TENANT_INATIVO_sem_gravar()
    {
        var inativo = await Dados.NovoTenantAsync(banco, "Cliente suspenso", ativo: false);
        var (coletor, _) = await Dados.NovoColetorComSegredoAsync(banco, inativo);
        // Token emitido enquanto o tenant estava ativo.
        var token = Acesso.TokenColetor(coletor, [inativo.IdPublico]);
        var antes = await DadosTenant.ImpressaoAsync(banco, inativo.Id);

        using var resposta = await EnviarAsync(token, NovoLote(inativo, [NovaVenda(1)]));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("TENANT_INATIVO", await Sessao.CodigoAsync(resposta));
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, inativo.Id));
    }

    [Fact]
    public async Task Lote_com_501_vendas_responde_413_LOTE_GRANDE_DEMAIS_sem_gravar()
    {
        var (tenant, token) = await AutorizadoAsync();
        var antes = await DadosTenant.ImpressaoAsync(banco, tenant.Id);

        using var resposta = await EnviarAsync(token, NovoLote(tenant, Enumerable.Range(1, 501).Select(i => NovaVenda(i)).ToList()));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        Assert.Equal("LOTE_GRANDE_DEMAIS", await Sessao.CodigoAsync(resposta));
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, tenant.Id));
    }

    [Fact]
    public async Task Corpo_acima_de_2_MB_responde_413_LOTE_GRANDE_DEMAIS_sem_gravar()
    {
        var (tenant, token) = await AutorizadoAsync();
        // 400 vendas (abaixo do limite de quantidade) com nomes de 6.000 caracteres: ~2,4 MB.
        var lote = NovoLote(tenant, Enumerable.Range(1, 400).Select(i => NovaVenda(i, nomeCliente: new string('x', 6000))).ToList());
        var antes = await DadosTenant.ImpressaoAsync(banco, tenant.Id);
        var corpo = System.Text.Json.JsonSerializer.Serialize(lote, System.Text.Json.JsonSerializerOptions.Web);
        Assert.True(Encoding.UTF8.GetByteCount(corpo) > Limites.BytesPorLote);

        using var resposta = await EnviarAsync(token, lote);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        Assert.Equal("LOTE_GRANDE_DEMAIS", await Sessao.CodigoAsync(resposta));
        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, tenant.Id));
    }

    [Fact]
    public async Task Token_de_usuario_responde_403()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);

        using var resposta = await EnviarAsync(Acesso.Token(admin, tenant), NovoLote(tenant, [NovaVenda(1)]));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
        Assert.Equal(0, await VendasNoBancoAsync(tenant));
    }

    [Fact]
    public async Task Lote_sem_batchId_responde_400_VALIDACAO()
    {
        var (tenant, token) = await AutorizadoAsync();

        using var resposta = await EnviarAsync(token, NovoLote(tenant, [NovaVenda(1)]) with { BatchId = Guid.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("VALIDACAO", await Sessao.CodigoAsync(resposta));
        Assert.Equal(0, await VendasNoBancoAsync(tenant));
    }
}
