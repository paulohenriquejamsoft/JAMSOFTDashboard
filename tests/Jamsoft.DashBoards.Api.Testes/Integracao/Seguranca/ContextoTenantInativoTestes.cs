using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Seguranca;

/// <summary>CTX-01: o SuperAdmin usa como contexto o <c>ID_PUBLICO</c> de um tenant inativo.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ContextoTenantInativoTestes(BancoDeTestes banco) : IAsyncLifetime
{
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

    private static List<string> Valores(JsonElement lista, string propriedade) =>
        [.. lista.EnumerateArray().Select(e => e.GetProperty(propriedade).GetString()!).Order()];

    [Fact]
    public async Task SuperAdmin_com_X_Tenant_de_tenant_inativo_lista_empresas_e_usuarios_dele()
    {
        var inativo = await Dados.NovoTenantAsync(banco, "Cliente Inativo", ativo: false);
        var outro = await Dados.NovoTenantAsync(banco);
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Empresas.Add(new Empresa { TenantId = inativo.Id, CodEmpresa = 1, Nome = "Do inativo" });
            contexto.Empresas.Add(new Empresa { TenantId = outro.Id, CodEmpresa = 1, Nome = "Do outro" });
            await contexto.SaveChangesAsync();
        }
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, inativo);
        var consulta = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, inativo);
        await Dados.NovoUsuarioAsync(banco, Perfil.Admin, outro);
        var token = await Acesso.TokenSuperAdminAsync(banco);

        using var empresas = await _cliente.SendAsync(
            Acesso.Requisicao(HttpMethod.Get, "/api/empresas", token, inativo.IdPublico));
        using var usuarios = await _cliente.SendAsync(
            Acesso.Requisicao(HttpMethod.Get, "/api/usuarios", token, inativo.IdPublico));

        Assert.Equal(HttpStatusCode.OK, empresas.StatusCode);
        Assert.Equal(["Do inativo"], Valores(await Sessao.JsonAsync(empresas), "nome"));
        Assert.Equal(HttpStatusCode.OK, usuarios.StatusCode);
        Assert.Equal(new[] { admin.Email, consulta.Email }.Order(), Valores(await Sessao.JsonAsync(usuarios), "email"));
    }
}
