using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using ColetorApi = Jamsoft.DashBoards.Api.Modulos.Coletores.Coletor;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Coletor.Testes.Integracao.Api;

/// <summary><see cref="ClienteApi"/> contra a API real em memória: COL-03, SYNC-04 e as traduções de recusa.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ClienteApiTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private ApiEmMemoria _api = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiEmMemoria(banco);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<(Tenant Tenant, ColetorApi Coletor, CredencialApi Credencial)> ColetorAutorizadoAsync()
    {
        var tenant = await DadosPlataforma.NovoTenantAsync(banco);
        var (coletor, credencial) = await DadosPlataforma.NovoColetorAsync(banco, tenant);
        return (tenant, coletor, credencial);
    }

    private static LoteVendas Lote(Tenant tenant, params int[] vendas) => new(Guid.NewGuid(), tenant.IdPublico, Guid.NewGuid(), "Vendas",
        "SRV01/ERP", [.. vendas.Select(id => new VendaContrato(id, "NFC-e", new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.FromHours(-3)),
            10m, 10, "Cliente", 3, "Vendedor", "Finalizada", "Loja 1", 1, 2))]);

    private Task<int> VendasNoBancoAsync(Tenant tenant) => DadosPlataforma.VendasAsync(banco, tenant);

    [Fact]
    public async Task Autentica_e_envia_um_lote()
    {
        var (tenant, _, credencial) = await ColetorAutorizadoAsync();
        using var cliente = new ClienteApi(credencial, TimeProvider.System, _api.Server.CreateHandler());

        var autenticacao = await cliente.AutenticarAsync();
        var envio = await cliente.EnviarLoteAsync(Lote(tenant, 1, 2));

        Assert.Equal(SituacaoResposta.Sucesso, autenticacao.Situacao);
        Assert.Equal([(tenant.IdPublico, "Cliente do Coletor")], autenticacao.Valor!.Select(t => (t.IdPublico, t.Nome)));
        Assert.Equal(SituacaoResposta.Sucesso, envio.Situacao);
        Assert.Equal((2, 0), (envio.Valor!.Inseridos, envio.Valor.Atualizados));
        Assert.Equal(2, await VendasNoBancoAsync(tenant));
    }

    [Fact]
    public async Task Token_expirado_e_renovado_uma_vez_e_o_lote_reenviado()
    {
        var (tenant, _, credencial) = await ColetorAutorizadoAsync();
        // A API emite o primeiro token uma hora atrás; o Coletor, com o relógio também atrasado, ainda o acha válido.
        var relogioApi = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        await using var api = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(relogioApi)));
        var transporte = new TransporteGravado(api.Server.CreateHandler(), TimeProvider.System);
        using var cliente = new ClienteApi(credencial, new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)), transporte);
        Assert.True((await cliente.AutenticarAsync()).Sucesso);
        relogioApi.SetUtcNow(DateTimeOffset.UtcNow);

        var envio = await cliente.EnviarLoteAsync(Lote(tenant, 1));

        Assert.Equal(SituacaoResposta.Sucesso, envio.Situacao);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], transporte.Da(ClienteApi.RotaToken).Select(c => c.Status));
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.OK], transporte.Da(ClienteApi.RotaLotes).Select(c => c.Status));
        Assert.Equal(1, await VendasNoBancoAsync(tenant));
    }

    [Fact]
    public async Task Credencial_revogada_resulta_em_credencial_invalida()
    {
        var (tenant, coletor, credencial) = await ColetorAutorizadoAsync();
        var transporte = new TransporteGravado(_api.Server.CreateHandler(), TimeProvider.System);
        using var cliente = new ClienteApi(credencial, TimeProvider.System, transporte);
        Assert.True((await cliente.EnviarLoteAsync(Lote(tenant, 1))).Sucesso);
        await using (var contexto = banco.CriarContexto())
        {
            await contexto.Coletores.Where(c => c.Id == coletor.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.Ativo, false));
        }

        _api.Services.GetRequiredService<StatusColetorCache>().Invalidar(coletor.IdPublico);

        var envio = await cliente.EnviarLoteAsync(Lote(tenant, 2));

        Assert.Equal(SituacaoResposta.CredencialInvalida, envio.Situacao);
        Assert.Equal("CREDENCIAL_INVALIDA", envio.Codigo);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Unauthorized], transporte.Da(ClienteApi.RotaToken).Select(c => c.Status));
        Assert.Equal(1, await VendasNoBancoAsync(tenant));
    }

    [Fact]
    public async Task Tenant_inativo_resulta_em_nao_autorizada()
    {
        var (tenant, _, credencial) = await ColetorAutorizadoAsync();
        using var cliente = new ClienteApi(credencial, TimeProvider.System, _api.Server.CreateHandler());
        Assert.True((await cliente.EnviarLoteAsync(Lote(tenant, 1))).Sucesso);
        await using (var contexto = banco.CriarContexto())
        {
            await contexto.Tenants.Where(t => t.Id == tenant.Id).ExecuteUpdateAsync(t => t.SetProperty(x => x.Ativo, false));
        }

        _api.Services.GetRequiredService<StatusAcessoCache>().InvalidarTenant(tenant.IdPublico);

        var envio = await cliente.EnviarLoteAsync(Lote(tenant, 2));

        Assert.Equal(SituacaoResposta.NaoAutorizada, envio.Situacao);
        Assert.Equal((403, "TENANT_INATIVO"), (envio.Status, envio.Codigo));
    }

    [Fact]
    public async Task Falha_transitoria_tres_vezes_chega_ao_sucesso_depois_de_2_4_e_8_segundos()
    {
        Assert.Equal([2, 4, 8, 16, 32], ClienteApi.IntervalosPadrao.Select(i => i.TotalSeconds));
        var (tenant, _, credencial) = await ColetorAutorizadoAsync();
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var transporte = new TransporteGravado(_api.Server.CreateHandler(), relogio)
        {
            Interceptar = (requisicao, numero) => requisicao.RequestUri!.AbsolutePath.EndsWith(ClienteApi.RotaLotes, StringComparison.Ordinal)
                && numero <= 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null,
        };
        using var cliente = new ClienteApi(credencial, relogio, transporte);

        var envio = cliente.EnviarLoteAsync(Lote(tenant, 1));
        // O relógio só anda quando o cliente está parado esperando: cada passo de 1 s dispara a espera vencida.
        for (var passos = 0; !envio.IsCompleted && passos < 200; passos++)
        {
            await Task.Delay(50);
            relogio.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(SituacaoResposta.Sucesso, (await envio).Situacao);
        var tentativas = transporte.Da(ClienteApi.RotaLotes);
        Assert.Equal([HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK],
            tentativas.Select(c => c.Status));
        Assert.Equal([2, 4, 8], tentativas.Zip(tentativas.Skip(1), (a, b) => (b.Em - a.Em).TotalSeconds));
        Assert.Equal(1, await VendasNoBancoAsync(tenant));
    }

    [Fact]
    public async Task Recusa_400_nao_e_repetida()
    {
        var (tenant, _, credencial) = await ColetorAutorizadoAsync();
        var transporte = new TransporteGravado(_api.Server.CreateHandler(), TimeProvider.System);
        using var cliente = new ClienteApi(credencial, TimeProvider.System, transporte);

        var envio = await cliente.EnviarLoteAsync(Lote(tenant, 1) with { Rotina = "" });

        Assert.Equal(SituacaoResposta.Recusada, envio.Situacao);
        Assert.Equal((400, "VALIDACAO"), (envio.Status, envio.Codigo));
        Assert.Single(transporte.Da(ClienteApi.RotaLotes));
    }
}
