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

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Autenticacao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class BloqueioTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string SenhaErrada = "SenhaErrada000";

    private readonly FakeTimeProvider _tempo = new(DateTimeOffset.UtcNow);
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        // Limite de requisições alto: aqui só o bloqueio por usuário está em teste.
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

    private async Task<Usuario> UsuarioAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        return await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant, comSenha: true);
    }

    private async Task<Usuario> NoBancoAsync(Usuario usuario)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
    }

    private async Task<HttpStatusCode> LoginAsync(Usuario usuario, string senha = Dados.Senha)
    {
        using var resposta = await Sessao.LoginAsync(_cliente, usuario.Email, senha);
        return resposta.StatusCode;
    }

    /// <summary>Quatro falhas (401) e a quinta, que bloqueia.</summary>
    private async Task<HttpResponseMessage> BloquearAsync(Usuario usuario)
    {
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(usuario, SenhaErrada));
        return await Sessao.LoginAsync(_cliente, usuario.Email, SenhaErrada);
    }

    [Fact]
    public async Task Quatro_falhas_e_um_sucesso_zeram_o_contador()
    {
        var usuario = await UsuarioAsync();
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(usuario, SenhaErrada));
        Assert.Equal(4, (await NoBancoAsync(usuario)).FalhasLogin);

        Assert.Equal(HttpStatusCode.OK, await LoginAsync(usuario));

        var depois = await NoBancoAsync(usuario);
        Assert.Equal(0, depois.FalhasLogin);
        Assert.Null(depois.BloqueadoAte);
        // Contador zerado: mais 4 falhas ainda não bloqueiam.
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(usuario, SenhaErrada));
    }

    [Fact]
    public async Task Quinta_falha_responde_423_com_bloqueadoAte_e_o_bloqueio_dura_ate_expirar()
    {
        var usuario = await UsuarioAsync();
        var esperado = _tempo.GetUtcNow().AddMinutes(15);

        using var quinta = await BloquearAsync(usuario);

        Assert.Equal((HttpStatusCode)423, quinta.StatusCode);
        var corpo = await Sessao.JsonAsync(quinta);
        Assert.Equal("USUARIO_BLOQUEADO", corpo.GetProperty("codigo").GetString());
        var bloqueadoAte = corpo.GetProperty("bloqueadoAte").GetDateTimeOffset();
        Assert.InRange(bloqueadoAte, esperado.AddSeconds(-1), esperado);
        Assert.Equal(bloqueadoAte.UtcDateTime, (await NoBancoAsync(usuario)).BloqueadoAte);

        _tempo.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal((HttpStatusCode)423, await LoginAsync(usuario, SenhaErrada));
    }

    [Fact]
    public async Task Login_correto_durante_o_bloqueio_responde_423()
    {
        var usuario = await UsuarioAsync();
        (await BloquearAsync(usuario)).Dispose();

        using var resposta = await Sessao.LoginAsync(_cliente, usuario.Email);

        Assert.Equal((HttpStatusCode)423, resposta.StatusCode);
        Assert.Equal("USUARIO_BLOQUEADO", await Sessao.CodigoAsync(resposta));
        Assert.Null(Sessao.SetCookieRefresh(resposta));
    }

    [Fact]
    public async Task Apos_15_minutos_o_login_correto_responde_200()
    {
        var usuario = await UsuarioAsync();
        (await BloquearAsync(usuario)).Dispose();

        _tempo.Advance(TimeSpan.FromMinutes(15));

        Assert.Equal(HttpStatusCode.OK, await LoginAsync(usuario));
        var depois = await NoBancoAsync(usuario);
        Assert.Equal(0, depois.FalhasLogin);
        Assert.Null(depois.BloqueadoAte);
    }

    [Fact]
    public async Task Vigesima_primeira_requisicao_de_login_no_minuto_responde_429()
    {
        // Configuração padrão: 20 requisições por minuto por IP.
        await using var api = new ApiFactory(banco);
        using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"{Guid.NewGuid():N}@teste.com";

        for (var i = 1; i <= 20; i++)
        {
            using var permitida = await Sessao.LoginAsync(cliente, email, SenhaErrada);
            Assert.Equal(HttpStatusCode.Unauthorized, permitida.StatusCode);
        }
        using var resposta = await Sessao.LoginAsync(cliente, email, SenhaErrada);

        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("MUITAS_REQUISICOES", await Sessao.CodigoAsync(resposta));
    }
}
