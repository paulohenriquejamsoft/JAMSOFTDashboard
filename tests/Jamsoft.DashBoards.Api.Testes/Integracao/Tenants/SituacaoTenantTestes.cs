using System.Net;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Tenants;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class SituacaoTenantTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private ApiFactory _api = null!;
    private HttpClient _cliente = null!;
    private string _token = "";

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco);
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _token = await Acesso.TokenSuperAdminAsync(banco);
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<(Tenant Tenant, Usuario Admin)> TenantComAdminAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Situação");
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant, comSenha: true);
        return (tenant, admin);
    }

    private Task<HttpResponseMessage> AlterarAsync(Tenant tenant, string acao) =>
        _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Post, $"/api/admin/tenants/{tenant.IdPublico}/{acao}", _token));

    private Task<HttpResponseMessage> EuAsync(string accessToken) =>
        _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/auth/eu", accessToken));

    [Fact]
    public async Task Apos_inativar_token_ja_emitido_e_login_recebem_403_ACESSO_DESATIVADO()
    {
        var (tenant, admin) = await TenantComAdminAsync();
        using var login = await Sessao.LoginAsync(_cliente, admin.Email);
        var token = (await Sessao.JsonAsync(login)).GetProperty("accessToken").GetString()!;
        // Situação ativa fica no cache antes da inativação.
        using (var antes = await EuAsync(token)) Assert.Equal(HttpStatusCode.OK, antes.StatusCode);

        using var inativar = await AlterarAsync(tenant, "inativar");

        Assert.Equal(HttpStatusCode.OK, inativar.StatusCode);
        Assert.False((await Sessao.JsonAsync(inativar)).GetProperty("ativo").GetBoolean());
        using var depois = await EuAsync(token);
        Assert.Equal(HttpStatusCode.Forbidden, depois.StatusCode);
        Assert.Equal("ACESSO_DESATIVADO", await Sessao.CodigoAsync(depois));
        using var novoLogin = await Sessao.LoginAsync(_cliente, admin.Email);
        Assert.Equal(HttpStatusCode.Forbidden, novoLogin.StatusCode);
        Assert.Equal("ACESSO_DESATIVADO", await Sessao.CodigoAsync(novoLogin));
    }

    [Fact]
    public async Task Inativar_preserva_os_dados_do_tenant()
    {
        var (tenant, admin) = await TenantComAdminAsync();
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Empresas.Add(new Empresa { TenantId = tenant.Id, CodEmpresa = 1, Nome = "Matriz" });
            await contexto.SaveChangesAsync();
        }

        using var inativar = await AlterarAsync(tenant, "inativar");

        Assert.Equal(HttpStatusCode.OK, inativar.StatusCode);
        await using var leitura = banco.CriarContexto(new TenantFixo(tenant.Id));
        var gravado = await leitura.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenant.Id);
        Assert.False(gravado.Ativo);
        Assert.Equal(tenant.Nome, gravado.Nome);
        Assert.Equal(tenant.Cnpj, gravado.Cnpj);
        Assert.Equal(["Matriz"], await leitura.Empresas.Select(e => e.Nome).ToListAsync());
        var usuario = await leitura.Usuarios.AsNoTracking().SingleAsync(u => u.TenantId == tenant.Id);
        Assert.Equal(admin.Email, usuario.Email);
        Assert.True(usuario.Ativo);
    }

    [Fact]
    public async Task Apos_reativar_login_responde_200()
    {
        var (tenant, admin) = await TenantComAdminAsync();
        using (var inativar = await AlterarAsync(tenant, "inativar")) Assert.Equal(HttpStatusCode.OK, inativar.StatusCode);
        using (var bloqueado = await Sessao.LoginAsync(_cliente, admin.Email))
            Assert.Equal(HttpStatusCode.Forbidden, bloqueado.StatusCode);

        using var reativar = await AlterarAsync(tenant, "reativar");

        Assert.Equal(HttpStatusCode.OK, reativar.StatusCode);
        Assert.True((await Sessao.JsonAsync(reativar)).GetProperty("ativo").GetBoolean());
        using var login = await Sessao.LoginAsync(_cliente, admin.Email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var eu = await EuAsync((await Sessao.JsonAsync(login)).GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, eu.StatusCode);
    }

    [Fact]
    public async Task Auditoria_registra_inativacao_e_reativacao()
    {
        var (tenant, _) = await TenantComAdminAsync();

        using (await AlterarAsync(tenant, "inativar")) { }
        using (await AlterarAsync(tenant, "reativar")) { }

        await using var contexto = banco.CriarContexto();
        var registros = await contexto.Auditoria.AsNoTracking()
            .Where(a => a.RecursoIdPublico == tenant.IdPublico)
            .OrderBy(a => a.Id)
            .ToListAsync();
        Assert.Equal(["TENANT_INATIVADO", "TENANT_REATIVADO"], registros.Select(r => r.Operacao));
        Assert.All(registros, r =>
        {
            Assert.Equal(tenant.IdPublico, r.TenantIdPublico);
            Assert.Equal("Cliente Situação", r.TenantNome);
            Assert.Equal("SuperAdmin", r.Perfil);
            Assert.Equal("Sucesso", r.Resultado);
        });
    }
}
