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
public sealed class RedefinirSenhaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string SenhaTemporaria = "Temporaria9876";

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

    private async Task<(Tenant Tenant, string Token, Usuario Alvo)> CenarioAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Senhas");
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        var alvo = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant, comSenha: true);
        return (tenant, Acesso.Token(admin, tenant), alvo);
    }

    private Task<HttpResponseMessage> RedefinirAsync(string token, Usuario alvo, string senha) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, $"/api/usuarios/{alvo.IdPublico}/redefinir-senha", token,
            new { senhaTemporaria = senha }));

    private async Task<Usuario> GravadoAsync(Usuario usuario)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
    }

    [Fact]
    public async Task Apos_redefinir_login_com_a_nova_senha_exige_troca()
    {
        var (_, token, alvo) = await CenarioAsync();

        using var resposta = await RedefinirAsync(token, alvo, SenhaTemporaria);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.True((await GravadoAsync(alvo)).TrocarSenha);
        using var login = await Sessao.LoginAsync(_cliente, alvo.Email, SenhaTemporaria);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await Sessao.JsonAsync(login)).GetProperty("trocarSenha").GetBoolean());
        using var senhaAntiga = await Sessao.LoginAsync(_cliente, alvo.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, senhaAntiga.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_anterior_a_redefinicao_responde_401()
    {
        var (_, token, alvo) = await CenarioAsync();
        using var login = await Sessao.LoginAsync(_cliente, alvo.Email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refresh = Sessao.CookieRefresh(login);

        using (var resposta = await RedefinirAsync(token, alvo, SenhaTemporaria))
            Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);

        using var renovacao = await Sessao.EnviarAsync(_cliente, "refresh", refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, renovacao.StatusCode);
        await using var contexto = banco.CriarContexto();
        Assert.All(await contexto.RefreshTokens.AsNoTracking().Where(r => r.UsuarioId == alvo.Id).ToListAsync(),
            r => Assert.NotNull(r.RevogadoEm));
    }

    [Fact]
    public async Task Senha_fora_da_politica_responde_400_VALIDACAO_sem_alterar()
    {
        var (_, token, alvo) = await CenarioAsync();

        using var resposta = await RedefinirAsync(token, alvo, "semdigitos");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty("senhaTemporaria", out _));
        var gravado = await GravadoAsync(alvo);
        Assert.Equal(alvo.SenhaHash, gravado.SenhaHash);
        Assert.False(gravado.TrocarSenha);
    }

    [Fact]
    public async Task Usuario_de_outro_tenant_responde_404_sem_alterar()
    {
        var (_, token, _) = await CenarioAsync();
        var outro = await Dados.NovoTenantAsync(banco);
        var alvo = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, outro);

        using var resposta = await RedefinirAsync(token, alvo, SenhaTemporaria);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("NAO_ENCONTRADO", await Sessao.CodigoAsync(resposta));
        var gravado = await GravadoAsync(alvo);
        Assert.Equal(alvo.SenhaHash, gravado.SenhaHash);
        Assert.False(gravado.TrocarSenha);
    }

    [Fact]
    public async Task Auditoria_registra_SENHA_REDEFINIDA_sem_a_senha()
    {
        var (tenant, token, alvo) = await CenarioAsync();

        using (var resposta = await RedefinirAsync(token, alvo, SenhaTemporaria))
            Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);

        await using var contexto = banco.CriarContexto();
        var registro = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == alvo.IdPublico);
        Assert.Equal("SENHA_REDEFINIDA", registro.Operacao);
        Assert.Equal(tenant.IdPublico, registro.TenantIdPublico);
        Assert.Equal("Admin", registro.Perfil);
        Assert.Equal("Sucesso", registro.Resultado);
        Assert.DoesNotContain(SenhaTemporaria, registro.Detalhes ?? "");
        Assert.DoesNotContain("senha", registro.Detalhes ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
