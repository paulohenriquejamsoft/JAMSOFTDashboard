using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Autenticacao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class RefreshTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private readonly FakeTimeProvider _tempo = new(DateTimeOffset.UtcNow);
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco).WithWebHostBuilder(b => b
            .UseSetting("Acesso:LimiteLoginPorMinuto", "1000")
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(_tempo)));
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<(Usuario Usuario, string Refresh)> LogadoAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant, comSenha: true);
        using var login = await Sessao.LoginAsync(_cliente, usuario.Email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (usuario, Sessao.CookieRefresh(login));
    }

    private async Task<List<Jamsoft.DashBoards.Api.Modulos.Autenticacao.RefreshToken>> TokensAsync(Usuario usuario)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.RefreshTokens.AsNoTracking().Where(r => r.UsuarioId == usuario.Id).ToListAsync();
    }

    [Fact]
    public async Task Refresh_valido_emite_novo_par_e_marca_o_antigo_como_usado()
    {
        var (usuario, antigo) = await LogadoAsync();

        using var resposta = await Sessao.EnviarAsync(_cliente, "refresh", antigo);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal(usuario.IdPublico.ToString(), new JsonWebToken(corpo.GetProperty("accessToken").GetString()).Subject);
        Assert.Equal("Admin", corpo.GetProperty("perfil").GetString());
        var novo = Sessao.CookieRefresh(resposta);
        Assert.NotEqual(antigo, novo);
        Assert.Contains("httponly", Sessao.SetCookieRefresh(resposta)!.ToLowerInvariant());

        var tokens = await TokensAsync(usuario);
        var registroAntigo = tokens.Single(t => t.Hash.SequenceEqual(RefreshTokens.Hash(antigo)));
        var registroNovo = tokens.Single(t => t.Hash.SequenceEqual(RefreshTokens.Hash(novo)));
        Assert.NotNull(registroAntigo.UsadoEm);
        Assert.Null(registroNovo.UsadoEm);
        Assert.Null(registroNovo.RevogadoEm);
        Assert.Equal(registroAntigo.Familia, registroNovo.Familia);
    }

    [Fact]
    public async Task Reuso_do_token_antigo_responde_401_e_revoga_todos_os_tokens_do_usuario()
    {
        var (usuario, antigo) = await LogadoAsync();
        using var outraSessao = await Sessao.LoginAsync(_cliente, usuario.Email);
        var deOutraSessao = Sessao.CookieRefresh(outraSessao);
        using var rotacao = await Sessao.EnviarAsync(_cliente, "refresh", antigo);
        var novo = Sessao.CookieRefresh(rotacao);

        using var reuso = await Sessao.EnviarAsync(_cliente, "refresh", antigo);

        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        Assert.Equal("NAO_AUTENTICADO", await Sessao.CodigoAsync(reuso));
        var tokens = await TokensAsync(usuario);
        Assert.Equal(3, tokens.Count);
        Assert.All(tokens, t => Assert.NotNull(t.RevogadoEm));
        using var comNovo = await Sessao.EnviarAsync(_cliente, "refresh", novo);
        using var comOutro = await Sessao.EnviarAsync(_cliente, "refresh", deOutraSessao);
        Assert.Equal(HttpStatusCode.Unauthorized, comNovo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, comOutro.StatusCode);
    }

    [Fact]
    public async Task Refresh_expirado_responde_401()
    {
        var (usuario, refresh) = await LogadoAsync();

        _tempo.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        using var resposta = await Sessao.EnviarAsync(_cliente, "refresh", refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("NAO_AUTENTICADO", await Sessao.CodigoAsync(resposta));
        Assert.Single(await TokensAsync(usuario));
    }

    [Fact]
    public async Task Refresh_sem_X_Requested_With_responde_400_e_nao_consome_o_token()
    {
        var (usuario, refresh) = await LogadoAsync();

        using var resposta = await Sessao.EnviarAsync(_cliente, "refresh", refresh, comCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("REQUISICAO_INVALIDA", await Sessao.CodigoAsync(resposta));
        Assert.Null(Assert.Single(await TokensAsync(usuario)).UsadoEm);
    }

    [Fact]
    public async Task Refresh_de_usuario_inativo_responde_401()
    {
        var (usuario, refresh) = await LogadoAsync();
        await using (var contexto = banco.CriarContexto())
            await contexto.Usuarios.Where(u => u.Id == usuario.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Ativo, false));

        using var resposta = await Sessao.EnviarAsync(_cliente, "refresh", refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("NAO_AUTENTICADO", await Sessao.CodigoAsync(resposta));
        Assert.Null(Sessao.SetCookieRefresh(resposta));
    }
}
