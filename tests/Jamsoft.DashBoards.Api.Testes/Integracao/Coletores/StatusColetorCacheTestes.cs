using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Coletores;

/// <summary>Revalidação do token de Coletor a cada requisição (COL-05, COL-09, AD-015).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class StatusColetorCacheTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string RotaColetor = "/teste/coletor";
    private const string RotaUsuario = "/teste/usuario";

    private readonly FakeTimeProvider _tempo = new(DateTimeOffset.UtcNow);
    private HostDeTeste _host = null!;
    private string _superAdmin = "";

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _host = await HostDeTeste.IniciarAsync(s => Acesso.Servicos(s, banco, _tempo), app =>
        {
            Acesso.Pipeline(app);
            app.MapColetores();
            app.MapGet(RotaColetor, (ITenantAtual atual) => Results.Ok(atual.TenantsAutorizadosColetor))
                .RequireAuthorization(Policies.Coletor);
            app.MapGet(RotaUsuario, () => Results.Ok());
        });
        _superAdmin = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin), null, _tempo);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private StatusColetorCache Cache => _host.Servicos.GetRequiredService<StatusColetorCache>();

    private async Task<(Coletor Coletor, Tenant[] Tenants, string Token)> ColetorComTokenAsync()
    {
        Tenant[] tenants = [await Dados.NovoTenantAsync(banco), await Dados.NovoTenantAsync(banco)];
        var (coletor, _) = await Dados.NovoColetorComSegredoAsync(banco, tenants);
        return (coletor, tenants, Acesso.TokenColetor(coletor, tenants.Select(t => t.IdPublico), _tempo));
    }

    private async Task<(HttpStatusCode Status, string? Codigo)> ChamarAsync(string rota, string token)
    {
        using var resposta = await _host.Cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, rota, token));
        var texto = await resposta.Content.ReadAsStringAsync();
        if (resposta.IsSuccessStatusCode || string.IsNullOrEmpty(texto)) return (resposta.StatusCode, null);
        using var json = JsonDocument.Parse(texto);
        return (resposta.StatusCode, json.RootElement.GetProperty("codigo").GetString());
    }

    private async Task<HttpStatusCode> AdminAsync(HttpMethod metodo, string rota, object? corpo = null)
    {
        using var requisicao = corpo is null
            ? Acesso.Requisicao(metodo, rota, _superAdmin)
            : Acesso.ComCorpo(metodo, rota, _superAdmin, corpo);
        using var resposta = await _host.Cliente.SendAsync(requisicao);
        return resposta.StatusCode;
    }

    [Fact]
    public async Task Token_valido_acessa_rota_de_Coletor_e_TenantsAutorizadosColetor_e_o_conjunto_da_claim()
    {
        var (_, tenants, token) = await ColetorComTokenAsync();

        using var resposta = await _host.Cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, RotaColetor, token));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var lidos = JsonSerializer.Deserialize<List<Guid>>(await resposta.Content.ReadAsStringAsync())!;
        Assert.Equal(tenants.Select(t => t.IdPublico).Order(), lidos.Order());
    }

    [Fact]
    public async Task Revogado_no_banco_o_token_antigo_recebe_401_em_ate_60_s_e_na_hora_apos_Invalidar()
    {
        var (coletor, _, token) = await ColetorComTokenAsync();
        var (outro, _, tokenOutro) = await ColetorComTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, token)).Status);
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, tokenOutro)).Status);

        // Revogados direto no banco, sem Invalidar: dentro do TTL vale o que está em cache.
        await using (var contexto = banco.CriarContexto())
            await contexto.Coletores.Where(c => c.Id == coletor.Id || c.Id == outro.Id)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.Ativo, false));
        _tempo.Advance(StatusColetorCache.Validade - TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, token)).Status);

        // Invalidado, o efeito é imediato só para aquele coletor.
        Cache.Invalidar(outro.IdPublico);
        Assert.Equal((HttpStatusCode.Unauthorized, "NAO_AUTENTICADO"), await ChamarAsync(RotaColetor, tokenOutro));
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, token)).Status);

        // Vencido o TTL de 60 s, consulta de novo e enxerga a revogação.
        _tempo.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal((HttpStatusCode.Unauthorized, "NAO_AUTENTICADO"), await ChamarAsync(RotaColetor, token));
    }

    [Fact]
    public async Task Revogar_pela_API_derruba_o_token_antigo_na_hora()
    {
        var (coletor, _, token) = await ColetorComTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, token)).Status);

        Assert.Equal(HttpStatusCode.OK, await AdminAsync(HttpMethod.Post, $"/api/admin/coletores/{coletor.IdPublico}/revogar"));

        Assert.Equal((HttpStatusCode.Unauthorized, "NAO_AUTENTICADO"), await ChamarAsync(RotaColetor, token));
    }

    [Fact]
    public async Task Apos_rotacionar_ou_trocar_tenants_o_token_antigo_recebe_401()
    {
        var (rotacionado, _, tokenRotacionado) = await ColetorComTokenAsync();
        var (trocado, tenants, tokenTrocado) = await ColetorComTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, tokenRotacionado)).Status);
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, tokenTrocado)).Status);
        string versao;
        await using (var contexto = banco.CriarContexto())
            versao = Convert.ToBase64String(
                (await contexto.Coletores.AsNoTracking().SingleAsync(c => c.Id == trocado.Id)).Versao);

        Assert.Equal(HttpStatusCode.OK,
            await AdminAsync(HttpMethod.Post, $"/api/admin/coletores/{rotacionado.IdPublico}/rotacionar"));
        Assert.Equal(HttpStatusCode.OK, await AdminAsync(HttpMethod.Put, $"/api/admin/coletores/{trocado.IdPublico}/tenants",
            new { tenants = new[] { tenants[0].IdPublico }, versao }));

        Assert.Equal((HttpStatusCode.Unauthorized, "NAO_AUTENTICADO"), await ChamarAsync(RotaColetor, tokenRotacionado));
        Assert.Equal((HttpStatusCode.Unauthorized, "NAO_AUTENTICADO"), await ChamarAsync(RotaColetor, tokenTrocado));
    }

    [Fact]
    public async Task Token_de_Coletor_valido_em_rota_de_usuario_responde_403_ACESSO_NEGADO()
    {
        var (_, _, token) = await ColetorComTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(RotaColetor, token)).Status);

        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_NEGADO"), await ChamarAsync(RotaUsuario, token));
        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_NEGADO"), await ChamarAsync("/api/admin/coletores", token));
    }
}
