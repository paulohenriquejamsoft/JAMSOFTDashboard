using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Usuarios;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class UsuariosEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/usuarios";
    private const string SenhaInicial = "Inicial12345";

    private ApiFactory _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco);
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private async Task<(Tenant Tenant, Usuario Usuario, string Token)> TenantComUsuarioAsync(Perfil perfil,
        string nome = "Cliente Usuários")
    {
        var tenant = await Dados.NovoTenantAsync(banco, nome);
        var usuario = await Dados.NovoUsuarioAsync(banco, perfil, tenant);
        return (tenant, usuario, Acesso.Token(usuario, tenant));
    }

    private static object NovoUsuario(string perfil = "Consulta", string? email = null, string senha = SenhaInicial) =>
        new { nome = "Maria Consulta", email = email ?? $"{Guid.NewGuid():N}@cliente.com", senhaInicial = senha, perfil };

    private Task<HttpResponseMessage> CriarAsync(string token, object corpo, Guid? xTenant = null)
    {
        var requisicao = Acesso.ComCorpo(HttpMethod.Post, Rota, token, corpo);
        if (xTenant is { } tenant) requisicao.Headers.Add(TenantAtual.CabecalhoTenant, tenant.ToString());
        return _cliente.SendAsync(requisicao);
    }

    private async Task<Usuario?> UsuarioPorEmailAsync(string email)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
    }

    [Fact]
    public async Task Admin_cria_Consulta_201_no_proprio_tenant_com_troca_de_senha_pendente()
    {
        var (tenant, _, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var email = $"{Guid.NewGuid():N}@cliente.com";

        using var resposta = await CriarAsync(token, NovoUsuario(email: $"  {email.ToUpperInvariant()} "));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("Consulta", corpo.GetProperty("perfil").GetString());
        Assert.True(corpo.GetProperty("trocarSenha").GetBoolean());
        Assert.Equal(email, corpo.GetProperty("email").GetString());

        var gravado = await UsuarioPorEmailAsync(email);
        Assert.NotNull(gravado);
        Assert.Equal(corpo.GetProperty("idPublico").GetGuid(), gravado.IdPublico);
        Assert.Equal(tenant.Id, gravado.TenantId);
        Assert.Equal(Perfil.Consulta, gravado.Perfil);
        Assert.True(gravado.TrocarSenha);
        Assert.True(gravado.Ativo);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            new PasswordHasher<Usuario>().VerifyHashedPassword(gravado, gravado.SenhaHash, SenhaInicial));

        await using var contexto = banco.CriarContexto();
        var auditoria = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == gravado.IdPublico);
        Assert.Equal("USUARIO_CRIADO", auditoria.Operacao);
        Assert.Equal(tenant.IdPublico, auditoria.TenantIdPublico);
        Assert.Equal("Admin", auditoria.Perfil);
        Assert.DoesNotContain(SenhaInicial, auditoria.Detalhes);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Coletor")]
    public async Task Perfil_fora_de_Admin_e_Consulta_responde_403_PERFIL_NAO_PERMITIDO(string perfil)
    {
        var (_, _, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var email = $"{Guid.NewGuid():N}@cliente.com";

        using var resposta = await CriarAsync(token, NovoUsuario(perfil, email));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("PERFIL_NAO_PERMITIDO", await Sessao.CodigoAsync(resposta));
        Assert.Null(await UsuarioPorEmailAsync(email));
    }

    [Fact]
    public async Task Email_de_outro_tenant_responde_409_EMAIL_EM_USO_sem_dados_do_outro_tenant()
    {
        var (tenant, _, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var outro = await Dados.NovoTenantAsync(banco, "Concorrente Secreto Ltda");
        var existente = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, outro);

        using var resposta = await CriarAsync(token, NovoUsuario(email: existente.Email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.Equal("EMAIL_EM_USO", JsonDocument.Parse(texto).RootElement.GetProperty("codigo").GetString());
        Assert.DoesNotContain(outro.IdPublico.ToString(), texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(outro.Nome, texto);
        Assert.DoesNotContain(existente.IdPublico.ToString(), texto, StringComparison.OrdinalIgnoreCase);
        var gravado = await UsuarioPorEmailAsync(existente.Email);
        Assert.Equal(outro.Id, gravado!.TenantId);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Usuarios.CountAsync(u => u.TenantId == tenant.Id));
    }

    [Fact]
    public async Task Consulta_listando_ou_criando_responde_403()
    {
        var (_, _, token) = await TenantComUsuarioAsync(Perfil.Consulta);
        var email = $"{Guid.NewGuid():N}@cliente.com";

        using var lista = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token));
        using var criacao = await CriarAsync(token, NovoUsuario(email: email));

        Assert.Equal(HttpStatusCode.Forbidden, lista.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(lista));
        Assert.Equal(HttpStatusCode.Forbidden, criacao.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(criacao));
        Assert.Null(await UsuarioPorEmailAsync(email));
    }

    [Fact]
    public async Task SuperAdmin_com_X_Tenant_cria_usuario_no_tenant_escolhido()
    {
        var escolhido = await Dados.NovoTenantAsync(banco);
        var token = await Acesso.TokenSuperAdminAsync(banco);
        var email = $"{Guid.NewGuid():N}@cliente.com";

        using var resposta = await CriarAsync(token, NovoUsuario("Admin", email), escolhido.IdPublico);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var gravado = await UsuarioPorEmailAsync(email);
        Assert.Equal(escolhido.Id, gravado!.TenantId);
        Assert.Equal(Perfil.Admin, gravado.Perfil);
        Assert.True(gravado.TrocarSenha);
    }

    [Fact]
    public async Task SuperAdmin_com_X_Tenant_lista_os_usuarios_do_tenant_escolhido()
    {
        var escolhido = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, escolhido);
        var consulta = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, escolhido);
        var outro = await Dados.NovoTenantAsync(banco);
        await Dados.NovoUsuarioAsync(banco, Perfil.Admin, outro);
        var token = await Acesso.TokenSuperAdminAsync(banco);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token, escolhido.IdPublico));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(new[] { admin.IdPublico, consulta.IdPublico }.Order(),
            (await Sessao.JsonAsync(resposta)).EnumerateArray().Select(u => u.GetProperty("idPublico").GetGuid()).Order());
    }

    [Fact]
    public async Task Lista_so_usuarios_do_tenant_sem_SENHA_HASH_ID_interno_ou_TENANT_ID()
    {
        var (tenant, admin, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var consulta = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);
        var outro = await Dados.NovoTenantAsync(banco);
        await Dados.NovoUsuarioAsync(banco, Perfil.Admin, outro);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var lista = await Sessao.JsonAsync(resposta);
        Assert.Equal([admin.IdPublico, consulta.IdPublico],
            lista.EnumerateArray().Select(u => u.GetProperty("idPublico").GetGuid()).Order());
        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hash-de-teste", texto);
        Assert.All(lista.EnumerateArray(), u =>
        {
            var propriedades = u.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToList();
            Assert.DoesNotContain("senhahash", propriedades);
            Assert.DoesNotContain("id", propriedades);
            Assert.DoesNotContain("tenantid", propriedades);
        });
    }

    [Fact]
    public async Task Senha_inicial_fora_da_politica_responde_400_VALIDACAO()
    {
        var (_, _, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var email = $"{Guid.NewGuid():N}@cliente.com";

        using var resposta = await CriarAsync(token, NovoUsuario(email: email, senha: "curta1"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty("senhaInicial", out _));
        Assert.Null(await UsuarioPorEmailAsync(email));
    }
}
