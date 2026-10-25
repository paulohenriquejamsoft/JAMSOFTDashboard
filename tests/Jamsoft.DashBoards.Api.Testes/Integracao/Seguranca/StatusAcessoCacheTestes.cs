using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Seguranca;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class StatusAcessoCacheTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/teste/protegida";

    private readonly FakeTimeProvider _tempo = new(DateTimeOffset.UtcNow);
    private HostDeTeste _host = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _host = await HostDeTeste.IniciarAsync(s => Acesso.Servicos(s, banco, _tempo), app =>
        {
            Acesso.Pipeline(app);
            app.MapGet(Rota, () => Results.Ok());
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private StatusAcessoCache Cache => _host.Servicos.GetRequiredService<StatusAcessoCache>();

    private async Task<(HttpStatusCode Status, string? Codigo)> ChamarAsync(string token)
    {
        using var resposta = await _host.Cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token));
        var texto = await resposta.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(texto)) return (resposta.StatusCode, null);
        using var json = JsonDocument.Parse(texto);
        return (resposta.StatusCode, json.RootElement.GetProperty("codigo").GetString());
    }

    private async Task InativarUsuarioNoBancoAsync(Usuario usuario)
    {
        await using var contexto = banco.CriarContexto();
        await contexto.Usuarios.Where(u => u.Id == usuario.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Ativo, false));
    }

    private async Task InativarTenantNoBancoAsync(Tenant tenant)
    {
        await using var contexto = banco.CriarContexto();
        await contexto.Tenants.Where(t => t.Id == tenant.Id).ExecuteUpdateAsync(t => t.SetProperty(x => x.Ativo, false));
    }

    [Fact]
    public async Task Usuario_inativado_com_token_valido_recebe_403_apos_Invalidar()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        var token = Acesso.Token(usuario, tenant, _tempo);
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(token)).Status);

        await InativarUsuarioNoBancoAsync(usuario);
        Cache.InvalidarUsuario(usuario.IdPublico);

        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_DESATIVADO"), await ChamarAsync(token));
    }

    [Fact]
    public async Task Tenant_inativado_derruba_todos_os_usuarios_dele()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant), tenant, _tempo);
        var consulta = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant), tenant, _tempo);
        var outroTenant = await Dados.NovoTenantAsync(banco);
        var deOutroTenant = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Admin, outroTenant), outroTenant, _tempo);
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(admin)).Status);

        await InativarTenantNoBancoAsync(tenant);
        Cache.InvalidarTenant(tenant.IdPublico);

        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_DESATIVADO"), await ChamarAsync(admin));
        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_DESATIVADO"), await ChamarAsync(consulta));
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(deOutroTenant)).Status);
    }

    [Fact]
    public async Task Usuario_e_tenant_ativos_seguem_com_200()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var token = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant), tenant, _tempo);

        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(token)).Status);
        _tempo.Advance(StatusAcessoCache.Validade + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(token)).Status);
    }

    [Fact]
    public async Task Situacao_e_lida_do_banco_no_maximo_uma_vez_por_TTL()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        var token = Acesso.Token(usuario, tenant, _tempo);
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(token)).Status);

        // Inativado direto no banco, sem Invalidar: dentro do TTL vale o que está em cache.
        await InativarUsuarioNoBancoAsync(usuario);
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(token)).Status);
        _tempo.Advance(StatusAcessoCache.Validade - TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await ChamarAsync(token)).Status);

        // Vencido o TTL, consulta de novo e enxerga a inativação.
        _tempo.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_DESATIVADO"), await ChamarAsync(token));
    }
}
