using System.Net;
using System.Net.Http.Json;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Autenticacao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class TrocarSenhaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string NovaSenha = "NovaSenha2026";

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

    /// <summary>Usuário com troca de senha pendente, logado pela API.</summary>
    private async Task<(Usuario Usuario, Tenant Tenant, string AccessToken)> PendenteAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Troca");
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant, trocarSenha: true, comSenha: true);
        using var login = await Sessao.LoginAsync(_cliente, usuario.Email);
        var corpo = await Sessao.JsonAsync(login);
        Assert.True(corpo.GetProperty("trocarSenha").GetBoolean());
        return (usuario, tenant, corpo.GetProperty("accessToken").GetString()!);
    }

    private Task<HttpResponseMessage> TrocarAsync(string accessToken, string senhaAtual, string novaSenha)
    {
        var requisicao = Acesso.Requisicao(HttpMethod.Post, "/api/auth/trocar-senha", accessToken);
        requisicao.Content = JsonContent.Create(new { senhaAtual, novaSenha });
        return _cliente.SendAsync(requisicao);
    }

    private Task<HttpResponseMessage> EuAsync(string accessToken) =>
        _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/auth/eu", accessToken));

    [Fact]
    public async Task Com_troca_pendente_outras_rotas_respondem_403_e_trocar_senha_responde_200()
    {
        var (usuario, _, token) = await PendenteAsync();

        using var outra = await EuAsync(token);
        Assert.Equal(HttpStatusCode.Forbidden, outra.StatusCode);
        Assert.Equal("TROCA_SENHA_PENDENTE", await Sessao.CodigoAsync(outra));

        using var troca = await TrocarAsync(token, Dados.Senha, NovaSenha);

        Assert.Equal(HttpStatusCode.OK, troca.StatusCode);
        var corpo = await Sessao.JsonAsync(troca);
        Assert.False(corpo.GetProperty("trocarSenha").GetBoolean());
        Assert.NotEqual(token, corpo.GetProperty("accessToken").GetString());
        await using var contexto = banco.CriarContexto();
        Assert.False((await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id)).TrocarSenha);
    }

    [Fact]
    public async Task Senha_atual_errada_responde_400_e_nao_troca()
    {
        var (usuario, _, token) = await PendenteAsync();

        using var resposta = await TrocarAsync(token, "SenhaAtualErrada1", NovaSenha);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty("senhaAtual", out _));
        await using var contexto = banco.CriarContexto();
        Assert.True((await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id)).TrocarSenha);
    }

    [Theory]
    [InlineData("Abcdefgh1")] // 9 caracteres
    [InlineData("SomenteLetras")] // sem dígito
    public async Task Nova_senha_fora_da_politica_responde_400_VALIDACAO(string novaSenha)
    {
        var (usuario, _, token) = await PendenteAsync();

        using var resposta = await TrocarAsync(token, Dados.Senha, novaSenha);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").GetProperty("novaSenha").GetArrayLength() > 0);
        await using var contexto = banco.CriarContexto();
        Assert.True((await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id)).TrocarSenha);
    }

    [Fact]
    public async Task Depois_da_troca_rotas_protegidas_respondem_200_e_vale_a_nova_senha()
    {
        var (usuario, _, token) = await PendenteAsync();
        using var troca = await TrocarAsync(token, Dados.Senha, NovaSenha);
        var novoToken = (await Sessao.JsonAsync(troca)).GetProperty("accessToken").GetString()!;

        using var protegida = await EuAsync(novoToken);
        Assert.Equal(HttpStatusCode.OK, protegida.StatusCode);

        using var senhaAntiga = await Sessao.LoginAsync(_cliente, usuario.Email);
        using var senhaNova = await Sessao.LoginAsync(_cliente, usuario.Email, NovaSenha);
        Assert.Equal(HttpStatusCode.Unauthorized, senhaAntiga.StatusCode);
        Assert.Equal(HttpStatusCode.OK, senhaNova.StatusCode);
        Assert.False((await Sessao.JsonAsync(senhaNova)).GetProperty("trocarSenha").GetBoolean());
    }

    [Fact]
    public async Task Eu_devolve_nome_email_perfil_e_tenant()
    {
        var (usuario, tenant, token) = await PendenteAsync();
        using var troca = await TrocarAsync(token, Dados.Senha, NovaSenha);
        var novoToken = (await Sessao.JsonAsync(troca)).GetProperty("accessToken").GetString()!;

        using var resposta = await EuAsync(novoToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal(usuario.Nome, corpo.GetProperty("nome").GetString());
        Assert.Equal(usuario.Email, corpo.GetProperty("email").GetString());
        Assert.Equal("Admin", corpo.GetProperty("perfil").GetString());
        Assert.Equal(tenant.IdPublico, corpo.GetProperty("tenant").GetProperty("idPublico").GetGuid());
        Assert.Equal("Cliente Troca", corpo.GetProperty("tenant").GetProperty("nome").GetString());
        Assert.False(corpo.TryGetProperty("senhaHash", out _));
    }
}
