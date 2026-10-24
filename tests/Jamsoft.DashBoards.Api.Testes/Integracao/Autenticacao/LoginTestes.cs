using System.Net;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Autenticacao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class LoginTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private readonly LogsCapturados _logs = new();
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco).WithWebHostBuilder(b => b.ConfigureLogging(l =>
        {
            l.SetMinimumLevel(LogLevel.Trace);
            l.AddProvider(_logs);
        }));
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    [Theory]
    [InlineData(Perfil.Admin)]
    [InlineData(Perfil.Consulta)]
    [InlineData(Perfil.SuperAdmin)]
    public async Task Login_valido_responde_200_com_campos_do_AUTH_01_e_cookie_de_refresh(Perfil perfil)
    {
        var tenant = perfil == Perfil.SuperAdmin ? null : await Dados.NovoTenantAsync(banco, "Cliente Login");
        var usuario = await Dados.NovoUsuarioAsync(banco, perfil, tenant, comSenha: true);
        var antes = DateTimeOffset.UtcNow;

        // E-mail normalizado: espaços e maiúsculas não importam.
        using var resposta = await Sessao.LoginAsync(_cliente, $"  {usuario.Email.ToUpperInvariant()} ");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        var accessToken = corpo.GetProperty("accessToken").GetString()!;
        var jwt = new JsonWebToken(accessToken);
        Assert.Equal(usuario.IdPublico.ToString(), jwt.Subject);
        var expiraEm = corpo.GetProperty("expiraEm").GetDateTimeOffset();
        Assert.InRange(expiraEm, antes.AddMinutes(15).AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(1));
        Assert.Equal(perfil.ToString(), corpo.GetProperty("perfil").GetString());
        Assert.False(corpo.GetProperty("trocarSenha").GetBoolean());
        if (tenant is null)
            Assert.Equal(System.Text.Json.JsonValueKind.Null, corpo.GetProperty("tenant").ValueKind);
        else
        {
            Assert.Equal(tenant.IdPublico, corpo.GetProperty("tenant").GetProperty("idPublico").GetGuid());
            Assert.Equal("Cliente Login", corpo.GetProperty("tenant").GetProperty("nome").GetString());
        }
        Assert.False(corpo.TryGetProperty("refreshToken", out _));

        var cookie = Sessao.SetCookieRefresh(resposta)!;
        var atributos = cookie.Split(';').Select(a => a.Trim().ToLowerInvariant()).ToList();
        Assert.Contains("httponly", atributos);
        Assert.Contains("secure", atributos);
        Assert.Contains("samesite=strict", atributos);
        Assert.Contains("path=/api/auth", atributos);

        // Refresh de 7 dias gravado só como SHA-256 do valor do cookie.
        var refresh = Sessao.CookieRefresh(resposta);
        Assert.Equal(48, refresh.Length);
        await using var contexto = banco.CriarContexto();
        var hash = RefreshTokens.Hash(refresh);
        var gravado = await contexto.RefreshTokens.AsNoTracking().SingleAsync(r => r.UsuarioId == usuario.Id);
        Assert.Equal(hash, gravado.Hash);
        Assert.InRange(gravado.ExpiraEm, antes.UtcDateTime.AddDays(7).AddSeconds(-2), DateTime.UtcNow.AddDays(7).AddSeconds(1));
        Assert.NotNull((await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id)).UltimoLoginEm);

        // Login de SuperAdmin é auditado (AUD-01); os outros não.
        var auditoria = await contexto.Auditoria.AsNoTracking().Where(r => r.UsuarioIdPublico == usuario.IdPublico).ToListAsync();
        if (perfil == Perfil.SuperAdmin)
        {
            var registro = Assert.Single(auditoria);
            Assert.Equal("LOGIN_SUPERADMIN", registro.Operacao);
            Assert.Equal("Sucesso", registro.Resultado);
            Assert.Equal(usuario.Email, registro.UsuarioEmail);
            Assert.Equal("SuperAdmin", registro.Perfil);
        }
        else Assert.Empty(auditoria);
    }

    [Fact]
    public async Task Email_inexistente_e_senha_errada_respondem_401_com_o_mesmo_corpo()
    {
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin, comSenha: true);

        using var inexistente = await Sessao.LoginAsync(_cliente, $"{Guid.NewGuid():N}@teste.com");
        using var senhaErrada = await Sessao.LoginAsync(_cliente, usuario.Email, "SenhaErrada999");

        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        var a = await Sessao.JsonAsync(inexistente);
        var b = await Sessao.JsonAsync(senhaErrada);
        Assert.Equal("CREDENCIAIS_INVALIDAS", a.GetProperty("codigo").GetString());
        Assert.Equal("Credenciais inválidas", a.GetProperty("title").GetString());
        // Mesmo corpo, exceto o traceId, que é por requisição.
        string SemTraceId(System.Text.Json.JsonElement json) => string.Join(",",
            json.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
        Assert.Equal(SemTraceId(a), SemTraceId(b));
        Assert.Null(Sessao.SetCookieRefresh(inexistente));
        Assert.Null(Sessao.SetCookieRefresh(senhaErrada));
    }

    [Fact]
    public async Task Usuario_inativo_recebe_403_ACESSO_DESATIVADO()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant, ativo: false, comSenha: true);

        using var resposta = await Sessao.LoginAsync(_cliente, usuario.Email);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("ACESSO_DESATIVADO", corpo.GetProperty("codigo").GetString());
        Assert.Equal("Acesso desativado", corpo.GetProperty("title").GetString());
        Assert.Null(Sessao.SetCookieRefresh(resposta));
    }

    [Fact]
    public async Task Usuario_de_tenant_inativo_recebe_403_ACESSO_DESATIVADO()
    {
        var tenant = await Dados.NovoTenantAsync(banco, ativo: false);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant, comSenha: true);

        using var resposta = await Sessao.LoginAsync(_cliente, usuario.Email);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_DESATIVADO", await Sessao.CodigoAsync(resposta));
        Assert.Null(Sessao.SetCookieRefresh(resposta));
    }

    [Fact]
    public async Task Logs_nao_contem_senha_nem_tokens()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant, comSenha: true);
        const string senhaErrada = "SenhaErradaNoLog42";

        using var falha = await Sessao.LoginAsync(_cliente, usuario.Email, senhaErrada);
        using var sucesso = await Sessao.LoginAsync(_cliente, usuario.Email);
        var accessToken = (await Sessao.JsonAsync(sucesso)).GetProperty("accessToken").GetString()!;
        var refresh = Sessao.CookieRefresh(sucesso);

        Assert.NotEmpty(_logs.Linhas);
        Assert.All(_logs.Linhas, linha =>
        {
            Assert.DoesNotContain(Dados.Senha, linha);
            Assert.DoesNotContain(senhaErrada, linha);
            Assert.DoesNotContain(accessToken, linha);
            Assert.DoesNotContain(refresh, linha);
        });
    }
}
