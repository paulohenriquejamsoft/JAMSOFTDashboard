using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Seguranca;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class AuditoriaContextoSuperAdminFilterTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/empresas";

    private readonly FakeTimeProvider _tempo = new(DateTimeOffset.UtcNow);
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(_tempo)));
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<(Guid SuperAdmin, string Token, Tenant Tenant)> SuperAdminAsync()
    {
        var superAdmin = await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin);
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Suporte");
        return (superAdmin.IdPublico, Acesso.Token(superAdmin, null), tenant);
    }

    private async Task AcessarAsync(string token, Guid? xTenant)
    {
        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token, xTenant));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    private async Task<List<RegistroAuditoria>> AcessosAsync(Guid tenant)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Auditoria.AsNoTracking()
            .Where(a => a.TenantIdPublico == tenant && a.Operacao == "ACESSO_CONTEXTO_TENANT")
            .ToListAsync();
    }

    [Fact]
    public async Task Primeiro_acesso_gera_um_registro_com_SuperAdmin_tenant_e_rota()
    {
        var (superAdmin, token, tenant) = await SuperAdminAsync();

        await AcessarAsync(token, tenant.IdPublico);

        var registro = Assert.Single(await AcessosAsync(tenant.IdPublico));
        Assert.Equal(superAdmin, registro.UsuarioIdPublico);
        Assert.Equal("SuperAdmin", registro.Perfil);
        Assert.Equal("Cliente Suporte", registro.TenantNome);
        Assert.Equal("Sucesso", registro.Resultado);
        Assert.Contains("\"rota\":\"/api/empresas/\"", registro.Detalhes);
    }

    [Fact]
    public async Task Segundo_acesso_dentro_de_5_minutos_nao_gera_registro()
    {
        var (_, token, tenant) = await SuperAdminAsync();
        await AcessarAsync(token, tenant.IdPublico);

        _tempo.Advance(TimeSpan.FromMinutes(5));
        await AcessarAsync(token, tenant.IdPublico);

        Assert.Single(await AcessosAsync(tenant.IdPublico));
    }

    [Fact]
    public async Task Acesso_apos_10_minutos_gera_novo_registro()
    {
        var (_, token, tenant) = await SuperAdminAsync();
        await AcessarAsync(token, tenant.IdPublico);

        _tempo.Advance(TimeSpan.FromMinutes(10));
        await AcessarAsync(token, tenant.IdPublico);

        Assert.Equal(2, (await AcessosAsync(tenant.IdPublico)).Count);
    }

    [Fact]
    public async Task Admin_do_tenant_nao_gera_registro()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);

        await AcessarAsync(Acesso.Token(admin, tenant), tenant.IdPublico);

        Assert.Empty(await AcessosAsync(tenant.IdPublico));
    }
}
