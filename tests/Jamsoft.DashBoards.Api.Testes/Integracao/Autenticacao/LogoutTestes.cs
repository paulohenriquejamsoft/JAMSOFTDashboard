using System.Net;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Autenticacao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class LogoutTestes(BancoDeTestes banco) : IAsyncLifetime
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

    private async Task<(int UsuarioId, string Email, string Refresh)> LogadoAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant, comSenha: true);
        using var login = await Sessao.LoginAsync(_cliente, usuario.Email);
        return (usuario.Id, usuario.Email, Sessao.CookieRefresh(login));
    }

    [Fact]
    public async Task Apos_logout_o_refresh_com_o_cookie_antigo_responde_401()
    {
        var (usuarioId, email, refresh) = await LogadoAsync();
        using var outraSessao = await Sessao.LoginAsync(_cliente, email);
        var deOutraSessao = Sessao.CookieRefresh(outraSessao);

        using var logout = await Sessao.EnviarAsync(_cliente, "logout", refresh);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        // Só a sessão encerrada cai: a outra segue renovando. (Conferido antes de reapresentar o cookie
        // antigo, porque apresentar um token revogado derruba todas as sessões, AUTH-05.)
        using var outra = await Sessao.EnviarAsync(_cliente, "refresh", deOutraSessao);
        Assert.Equal(HttpStatusCode.OK, outra.StatusCode);

        using var refreshAntigo = await Sessao.EnviarAsync(_cliente, "refresh", refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAntigo.StatusCode);
        await using (var contexto = banco.CriarContexto())
        {
            var revogado = await contexto.RefreshTokens.AsNoTracking()
                .SingleAsync(r => r.Hash == RefreshTokens.Hash(refresh));
            Assert.NotNull(revogado.RevogadoEm);
            Assert.Equal(usuarioId, revogado.UsuarioId);
        }
    }

    [Fact]
    public async Task Resposta_do_logout_expira_o_cookie_de_refresh()
    {
        var (_, _, refresh) = await LogadoAsync();

        using var logout = await Sessao.EnviarAsync(_cliente, "logout", refresh);

        var cookie = Sessao.SetCookieRefresh(logout)!;
        var atributos = cookie.Split(';').Select(a => a.Trim().ToLowerInvariant()).ToList();
        Assert.Equal($"{RefreshTokens.Cookie}=", atributos[0]);
        Assert.Contains("expires=thu, 01 jan 1970 00:00:00 gmt", atributos);
        Assert.Contains("path=/api/auth", atributos);
    }

    [Fact]
    public async Task Logout_sem_cookie_responde_204_e_repetir_o_logout_tambem()
    {
        var (_, _, refresh) = await LogadoAsync();

        using var semCookie = await Sessao.EnviarAsync(_cliente, "logout", refresh: null, comCsrf: false);
        using var primeiro = await Sessao.EnviarAsync(_cliente, "logout", refresh);
        using var repetido = await Sessao.EnviarAsync(_cliente, "logout", refresh);

        Assert.Equal(HttpStatusCode.NoContent, semCookie.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, primeiro.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repetido.StatusCode);
    }
}
