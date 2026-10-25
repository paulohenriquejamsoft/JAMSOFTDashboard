using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Seguranca;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class PoliciesTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string RotaComum = "/teste/comum";
    private const string RotaAdmin = "/teste/admin";
    private const string RotaTrocaSenha = "/teste/trocar-senha";

    private HostDeTeste _host = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _host = await HostDeTeste.IniciarAsync(s => Acesso.Servicos(s, banco), app =>
        {
            Acesso.Pipeline(app);
            app.MapGet(RotaComum, () => Results.Ok());
            app.MapGet(RotaAdmin, () => Results.Ok()).RequireAuthorization(Policies.AdminTenant);
            app.MapPost(RotaTrocaSenha, () => Results.Ok()).AllowTrocaSenhaPendente();
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<(HttpStatusCode Status, string? Codigo)> EnviarAsync(HttpMethod metodo, string rota,
        string? token, Guid? xTenant = null)
    {
        using var resposta = await _host.Cliente.SendAsync(Acesso.Requisicao(metodo, rota, token, xTenant));
        var texto = await resposta.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(texto)) return (resposta.StatusCode, null);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(texto);
        return (resposta.StatusCode, json.RootElement.GetProperty("codigo").GetString());
    }

    private async Task<string> TokenAsync(Perfil perfil, bool trocarSenha = false, TimeProvider? tempo = null)
    {
        var tenant = perfil == Perfil.SuperAdmin ? null : await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, perfil, tenant, trocarSenha);
        return Acesso.Token(usuario, tenant, tempo);
    }

    [Fact]
    public async Task Sem_token_responde_401_NAO_AUTENTICADO()
    {
        Assert.Equal((HttpStatusCode.Unauthorized, "NAO_AUTENTICADO"), await EnviarAsync(HttpMethod.Get, RotaComum, null));
    }

    [Fact]
    public async Task Token_expirado_responde_401_NAO_AUTENTICADO()
    {
        var umaHoraAtras = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        var token = await TokenAsync(Perfil.Admin, tempo: umaHoraAtras);

        Assert.Equal((HttpStatusCode.Unauthorized, "NAO_AUTENTICADO"), await EnviarAsync(HttpMethod.Get, RotaComum, token));
    }

    [Fact]
    public async Task Perfil_sem_permissao_responde_403_ACESSO_NEGADO_e_perfil_certo_passa()
    {
        var consulta = await TokenAsync(Perfil.Consulta);
        var admin = await TokenAsync(Perfil.Admin);

        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_NEGADO"), await EnviarAsync(HttpMethod.Get, RotaAdmin, consulta));
        Assert.Equal(HttpStatusCode.OK, (await EnviarAsync(HttpMethod.Get, RotaAdmin, admin)).Status);
    }

    [Fact]
    public async Task Troca_de_senha_pendente_responde_403_TROCA_SENHA_PENDENTE()
    {
        var token = await TokenAsync(Perfil.Admin, trocarSenha: true);

        Assert.Equal((HttpStatusCode.Forbidden, "TROCA_SENHA_PENDENTE"), await EnviarAsync(HttpMethod.Get, RotaComum, token));
        Assert.Equal((HttpStatusCode.Forbidden, "TROCA_SENHA_PENDENTE"), await EnviarAsync(HttpMethod.Get, RotaAdmin, token));
    }

    [Fact]
    public async Task Troca_de_senha_pendente_acessa_rota_liberada()
    {
        var token = await TokenAsync(Perfil.Consulta, trocarSenha: true);

        Assert.Equal(HttpStatusCode.OK, (await EnviarAsync(HttpMethod.Post, RotaTrocaSenha, token)).Status);
    }

    [Fact]
    public async Task Token_de_Coletor_responde_403_em_rota_de_usuario()
    {
        // Credencial de Coletor não é usuário do portal: não existe em USUARIO. Precisa ser uma credencial
        // real e ativa (COLETOR), senão a revalidação do token (StatusColetorCache) já responde 401.
        var coletor = await Dados.NovoColetorAsync(banco);
        var token = Acesso.TokenColetor(coletor, [(await Dados.NovoTenantAsync(banco)).IdPublico]);

        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_NEGADO"), await EnviarAsync(HttpMethod.Get, RotaComum, token));
        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_NEGADO"), await EnviarAsync(HttpMethod.Get, RotaAdmin, token));
    }

    [Fact]
    public async Task SuperAdmin_sem_tenant_em_AdminTenant_responde_400_TENANT_NAO_SELECIONADO()
    {
        var token = await TokenAsync(Perfil.SuperAdmin);
        var tenant = await Dados.NovoTenantAsync(banco);

        Assert.Equal((HttpStatusCode.BadRequest, "TENANT_NAO_SELECIONADO"), await EnviarAsync(HttpMethod.Get, RotaAdmin, token));
        Assert.Equal(HttpStatusCode.OK, (await EnviarAsync(HttpMethod.Get, RotaAdmin, token, tenant.IdPublico)).Status);
    }
}
