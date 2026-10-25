using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Coletores;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ColetoresEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/admin/coletores";

    private ApiFactory _api = null!;
    private HttpClient _cliente = null!;
    private string _token = "";

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco);
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _token = await Acesso.TokenSuperAdminAsync(banco);
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    private Task<HttpResponseMessage> CriarAsync(object corpo, string? token = null) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token ?? _token, corpo));

    private static async Task<(HttpStatusCode Status, string? Codigo, JsonElement Json)> LerAsync(HttpResponseMessage resposta)
    {
        var json = await Sessao.JsonAsync(resposta);
        return (resposta.StatusCode, json.TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null, json);
    }

    [Fact]
    public async Task Cria_coletor_e_responde_201_com_idPublico_clientId_e_clientSecret()
    {
        var a = await Dados.NovoTenantAsync(banco, "Cliente A");
        var b = await Dados.NovoTenantAsync(banco, "Cliente B");

        using var resposta = await CriarAsync(new { nome = "Servidor Loja", tenants = new[] { a.IdPublico, b.IdPublico } });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        var idPublico = json.GetProperty("idPublico").GetGuid();
        var clientId = json.GetProperty("clientId").GetGuid();
        Assert.NotEqual(Guid.Empty, idPublico);
        Assert.NotEqual(Guid.Empty, clientId);
        Assert.Equal(43, json.GetProperty("clientSecret").GetString()!.Length);
        Assert.Equal($"{Rota}/{idPublico}", resposta.Headers.Location?.ToString());

        await using var contexto = banco.CriarContexto();
        var coletor = await contexto.Coletores.AsNoTracking().SingleAsync(c => c.IdPublico == idPublico);
        Assert.Equal("Servidor Loja", coletor.Nome);
        Assert.Equal(clientId, coletor.ClientId);
        Assert.True(coletor.Ativo);
        Assert.Equal(1, coletor.VersaoCredencial);
        var autorizados = await contexto.ColetoresTenant.IgnoreQueryFilters()
            .Where(ct => ct.ColetorId == coletor.Id).Select(ct => ct.TenantId).OrderBy(id => id).ToListAsync();
        Assert.Equal(new[] { a.Id, b.Id }.Order(), autorizados);
    }

    [Fact]
    public async Task Banco_guarda_o_SHA256_do_secret_e_nunca_o_secret()
    {
        var tenant = await Dados.NovoTenantAsync(banco);

        using var resposta = await CriarAsync(new { nome = "Coletor Hash", tenants = new[] { tenant.IdPublico } });

        var json = await Sessao.JsonAsync(resposta);
        var segredo = json.GetProperty("clientSecret").GetString()!;
        var idPublico = json.GetProperty("idPublico").GetGuid();
        await using var contexto = banco.CriarContexto();
        var coletor = await contexto.Coletores.AsNoTracking().SingleAsync(c => c.IdPublico == idPublico);
        // A única coluna com o segredo é SECRET_HASH, e ela tem exatamente o SHA-256 (32 bytes), não o texto.
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(segredo)), coletor.SecretHash);
        Assert.Equal("Coletor Hash", coletor.Nome);
    }

    [Fact]
    public async Task Sem_tenant_responde_400_VALIDACAO()
    {
        using var resposta = await CriarAsync(new { nome = "Sem Tenant", tenants = Array.Empty<Guid>() });

        var (status, codigo, json) = await LerAsync(resposta);
        Assert.Equal((HttpStatusCode.BadRequest, "VALIDACAO"), (status, codigo));
        Assert.True(json.GetProperty("erros").TryGetProperty("tenants", out _));
    }

    [Fact]
    public async Task Tenant_inativo_responde_400_VALIDACAO_e_nada_e_gravado()
    {
        var ativo = await Dados.NovoTenantAsync(banco);
        var inativo = await Dados.NovoTenantAsync(banco, ativo: false);
        var nome = $"Inativo {Guid.NewGuid():N}"[..40];

        using var resposta = await CriarAsync(new { nome, tenants = new[] { ativo.IdPublico, inativo.IdPublico } });

        var (status, codigo, json) = await LerAsync(resposta);
        Assert.Equal((HttpStatusCode.BadRequest, "VALIDACAO"), (status, codigo));
        Assert.True(json.GetProperty("erros").TryGetProperty("tenants", out _));
        await using var contexto = banco.CriarContexto();
        Assert.False(await contexto.Coletores.AnyAsync(c => c.Nome == nome));
    }

    [Fact]
    public async Task Nome_com_2_caracteres_responde_400_VALIDACAO()
    {
        var tenant = await Dados.NovoTenantAsync(banco);

        using var resposta = await CriarAsync(new { nome = "AB", tenants = new[] { tenant.IdPublico } });

        var (status, codigo, json) = await LerAsync(resposta);
        Assert.Equal((HttpStatusCode.BadRequest, "VALIDACAO"), (status, codigo));
        Assert.True(json.GetProperty("erros").TryGetProperty("nome", out _));
    }

    [Fact]
    public async Task Admin_responde_403_ACESSO_NEGADO()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant), tenant);

        using var resposta = await CriarAsync(new { nome = "Pelo Admin", tenants = new[] { tenant.IdPublico } }, admin);

        var (status, codigo, _) = await LerAsync(resposta);
        Assert.Equal((HttpStatusCode.Forbidden, "ACESSO_NEGADO"), (status, codigo));
    }

    [Fact]
    public async Task Auditoria_registra_COLETOR_CRIADO_sem_o_secret()
    {
        var tenant = await Dados.NovoTenantAsync(banco);

        using var resposta = await CriarAsync(new { nome = "Coletor Auditado", tenants = new[] { tenant.IdPublico } });

        var json = await Sessao.JsonAsync(resposta);
        var idPublico = json.GetProperty("idPublico").GetGuid();
        var segredo = json.GetProperty("clientSecret").GetString()!;
        await using var contexto = banco.CriarContexto();
        var registro = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == idPublico);
        Assert.Equal("COLETOR_CRIADO", registro.Operacao);
        Assert.Equal("SuperAdmin", registro.Perfil);
        Assert.Equal("Sucesso", registro.Resultado);
        Assert.Contains("Coletor Auditado", registro.Detalhes);
        Assert.Contains(tenant.IdPublico.ToString(), registro.Detalhes);
        Assert.DoesNotContain(segredo, registro.Detalhes);
        Assert.DoesNotContain("secret", registro.Detalhes, StringComparison.OrdinalIgnoreCase);
    }
}
