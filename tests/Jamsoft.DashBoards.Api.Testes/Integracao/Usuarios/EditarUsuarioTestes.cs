using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Usuarios;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class EditarUsuarioTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco).WithWebHostBuilder(b => b.UseSetting("Acesso:LimiteLoginPorMinuto", "1000"));
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<(Tenant Tenant, Usuario Admin, string Token)> TenantComAdminAsync(string nome = "Cliente Edição")
    {
        var tenant = await Dados.NovoTenantAsync(banco, nome);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        return (tenant, admin, Acesso.Token(admin, tenant));
    }

    private Task<HttpResponseMessage> EditarAsync(string token, Usuario alvo, string nome, string perfil, bool ativo,
        byte[]? versao = null) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, $"/api/usuarios/{alvo.IdPublico}", token,
            new { nome, perfil, ativo, versao = Convert.ToBase64String(versao ?? alvo.Versao) }));

    private async Task<Usuario> GravadoAsync(Usuario usuario)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
    }

    [Fact]
    public async Task Editar_nome_e_perfil_responde_200_e_registra_auditoria()
    {
        var (tenant, _, token) = await TenantComAdminAsync();
        var alvo = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);

        using var resposta = await EditarAsync(token, alvo, "Novo Nome", "Admin", ativo: true);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("Novo Nome", corpo.GetProperty("nome").GetString());
        Assert.Equal("Admin", corpo.GetProperty("perfil").GetString());
        var gravado = await GravadoAsync(alvo);
        Assert.Equal("Novo Nome", gravado.Nome);
        Assert.Equal(Perfil.Admin, gravado.Perfil);
        Assert.True(gravado.Ativo);
        await using var contexto = banco.CriarContexto();
        var auditoria = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == alvo.IdPublico);
        Assert.Equal("USUARIO_ALTERADO", auditoria.Operacao);
        Assert.Equal(tenant.IdPublico, auditoria.TenantIdPublico);
        Assert.Equal("Sucesso", auditoria.Resultado);
        Assert.Contains("\"perfil\":\"Admin\"", auditoria.Detalhes);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Coletor")]
    public async Task Promover_a_perfil_nao_permitido_responde_403_PERFIL_NAO_PERMITIDO_sem_alterar(string perfil)
    {
        var (tenant, _, token) = await TenantComAdminAsync();
        var alvo = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);

        using var resposta = await EditarAsync(token, alvo, "Promovido", perfil, ativo: true);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("PERFIL_NAO_PERMITIDO", await Sessao.CodigoAsync(resposta));
        var gravado = await GravadoAsync(alvo);
        Assert.Equal(alvo.Nome, gravado.Nome);
        Assert.Equal(Perfil.Consulta, gravado.Perfil);
        Assert.True(gravado.Ativo);
        Assert.Equal(alvo.Versao, gravado.Versao);
        await using var contexto = banco.CriarContexto();
        Assert.False(await contexto.Auditoria.AsNoTracking().AnyAsync(a => a.RecursoIdPublico == alvo.IdPublico));
    }

    [Fact]
    public async Task Rebaixar_o_unico_Admin_responde_422_ULTIMO_ADMIN_com_a_mensagem_do_spec()
    {
        var (tenant, admin, token) = await TenantComAdminAsync();
        await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);

        using var resposta = await EditarAsync(token, admin, admin.Nome, "Consulta", ativo: true);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("ULTIMO_ADMIN", corpo.GetProperty("codigo").GetString());
        Assert.Equal("O tenant precisa de ao menos um Admin ativo", corpo.GetProperty("title").GetString());
        var gravado = await GravadoAsync(admin);
        Assert.Equal(Perfil.Admin, gravado.Perfil);
        Assert.True(gravado.Ativo);
    }

    [Fact]
    public async Task Inativar_usuario_revoga_refresh_tokens_e_derruba_o_access_token()
    {
        var (tenant, _, token) = await TenantComAdminAsync();
        var alvo = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant, comSenha: true);
        using var login = await Sessao.LoginAsync(_cliente, alvo.Email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refresh = Sessao.CookieRefresh(login);
        var accessToken = (await Sessao.JsonAsync(login)).GetProperty("accessToken").GetString()!;
        // Situação ativa fica no cache antes da inativação.
        using (var antes = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/auth/eu", accessToken)))
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);

        using var resposta = await EditarAsync(token, await GravadoAsync(alvo), alvo.Nome, "Consulta", ativo: false);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False((await Sessao.JsonAsync(resposta)).GetProperty("ativo").GetBoolean());
        using var renovacao = await Sessao.EnviarAsync(_cliente, "refresh", refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, renovacao.StatusCode);
        using var depois = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/auth/eu", accessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode);
        await using var contexto = banco.CriarContexto();
        var tokens = await contexto.RefreshTokens.AsNoTracking().Where(r => r.UsuarioId == alvo.Id).ToListAsync();
        Assert.NotEmpty(tokens);
        Assert.All(tokens, r => Assert.NotNull(r.RevogadoEm));
    }

    [Fact]
    public async Task Usuario_de_outro_tenant_responde_404_sem_alterar()
    {
        var (_, _, token) = await TenantComAdminAsync();
        var outro = await Dados.NovoTenantAsync(banco);
        var alvo = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, outro);

        using var resposta = await EditarAsync(token, alvo, "Invadido", "Admin", ativo: false);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("NAO_ENCONTRADO", await Sessao.CodigoAsync(resposta));
        var gravado = await GravadoAsync(alvo);
        Assert.Equal(alvo.Nome, gravado.Nome);
        Assert.Equal(Perfil.Consulta, gravado.Perfil);
        Assert.True(gravado.Ativo);
    }

    [Fact]
    public async Task Versao_antiga_responde_409_CONFLITO_VERSAO()
    {
        var (tenant, _, token) = await TenantComAdminAsync();
        var alvo = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);
        var versaoLida = alvo.Versao;

        using var primeira = await EditarAsync(token, alvo, "Primeira edição", "Consulta", ativo: true, versaoLida);
        using var segunda = await EditarAsync(token, alvo, "Edição atrasada", "Consulta", ativo: true, versaoLida);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("CONFLITO_VERSAO", await Sessao.CodigoAsync(segunda));
        Assert.Equal("Primeira edição", (await GravadoAsync(alvo)).Nome);
    }
}
