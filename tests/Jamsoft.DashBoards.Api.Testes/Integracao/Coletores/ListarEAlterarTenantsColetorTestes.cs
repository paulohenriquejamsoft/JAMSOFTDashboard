using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Coletores;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ListarEAlterarTenantsColetorTestes(BancoDeTestes banco) : IAsyncLifetime
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

    /// <summary>Cria pelo POST e devolve o ID público e o secret exibido.</summary>
    private async Task<(Guid IdPublico, string Segredo)> CriarAsync(string nome, params Tenant[] tenants)
    {
        using var resposta = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, _token,
            new { nome, tenants = tenants.Select(t => t.IdPublico) }));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        return (json.GetProperty("idPublico").GetGuid(), json.GetProperty("clientSecret").GetString()!);
    }

    /// <summary>Percorre todas as páginas e devolve o texto bruto de cada uma.</summary>
    private async Task<List<string>> PaginasAsync()
    {
        var paginas = new List<string>();
        for (var pagina = 1; ; pagina++)
        {
            using var resposta = await _cliente.SendAsync(
                Acesso.Requisicao(HttpMethod.Get, $"{Rota}?pagina={pagina}&tamanho=100", _token));
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            var texto = await resposta.Content.ReadAsStringAsync();
            paginas.Add(texto);
            using var json = JsonDocument.Parse(texto);
            if (pagina * 100 >= json.RootElement.GetProperty("total").GetInt32()) return paginas;
        }
    }

    private async Task<JsonElement> ItemDaListaAsync(Guid idPublico)
    {
        foreach (var texto in await PaginasAsync())
        {
            using var json = JsonDocument.Parse(texto);
            foreach (var item in json.RootElement.GetProperty("itens").EnumerateArray())
                if (item.GetProperty("idPublico").GetGuid() == idPublico) return item.Clone();
        }
        throw new InvalidOperationException($"Coletor {idPublico} fora da lista.");
    }

    private async Task<(string Versao, int VersaoCredencial, List<int> Tenants)> NoBancoAsync(Guid idPublico)
    {
        await using var contexto = banco.CriarContexto();
        var coletor = await contexto.Coletores.AsNoTracking().SingleAsync(c => c.IdPublico == idPublico);
        var tenants = await contexto.ColetoresTenant.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
            .Where(x => x.ColetorId == coletor.Id).Select(x => x.TenantId).OrderBy(id => id).ToListAsync();
        return (Convert.ToBase64String(coletor.Versao), coletor.VersaoCredencial, tenants);
    }

    private Task<HttpResponseMessage> AlterarAsync(Guid idPublico, object corpo, string? token = null) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, $"{Rota}/{idPublico}/tenants", token ?? _token, corpo));

    [Fact]
    public async Task Lista_traz_os_tenants_com_id_e_nome_e_nunca_secret_ou_hash()
    {
        var a = await Dados.NovoTenantAsync(banco, "Alfa Lista");
        var b = await Dados.NovoTenantAsync(banco, "Beta Lista");
        var (idPublico, segredo) = await CriarAsync("Coletor Listado", b, a);

        var item = await ItemDaListaAsync(idPublico);

        Assert.Equal("Coletor Listado", item.GetProperty("nome").GetString());
        Assert.NotEqual(Guid.Empty, item.GetProperty("clientId").GetGuid());
        Assert.True(item.GetProperty("ativo").GetBoolean());
        var tenants = item.GetProperty("tenants").EnumerateArray()
            .Select(t => (t.GetProperty("idPublico").GetGuid(), t.GetProperty("nome").GetString())).ToList();
        Assert.Equal([(a.IdPublico, "Alfa Lista"), (b.IdPublico, "Beta Lista")], tenants);
        var propriedades = item.EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain(propriedades, p => p.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || p.Contains("hash", StringComparison.OrdinalIgnoreCase));
        var hash = Convert.ToBase64String(Api.Modulos.Coletores.SegredoColetor.Hash(segredo));
        Assert.All(await PaginasAsync(), texto =>
        {
            Assert.DoesNotContain(segredo, texto);
            Assert.DoesNotContain(hash, texto);
        });
    }

    [Fact]
    public async Task PUT_troca_a_lista_e_incrementa_VERSAO_CREDENCIAL()
    {
        var a = await Dados.NovoTenantAsync(banco, "Antigo");
        var b = await Dados.NovoTenantAsync(banco, "Mantido");
        var c = await Dados.NovoTenantAsync(banco, "Novo");
        var (idPublico, _) = await CriarAsync("Coletor Troca", a, b);
        var (versao, versaoCredencial, _) = await NoBancoAsync(idPublico);

        using var resposta = await AlterarAsync(idPublico, new { tenants = new[] { b.IdPublico, c.IdPublico }, versao });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var depois = await NoBancoAsync(idPublico);
        Assert.Equal(new[] { b.Id, c.Id }.Order(), depois.Tenants);
        Assert.Equal(versaoCredencial + 1, depois.VersaoCredencial);
        var json = await Sessao.JsonAsync(resposta);
        Assert.Equal(depois.Versao, json.GetProperty("versao").GetString());
        Assert.Equal(["Mantido", "Novo"],
            json.GetProperty("tenants").EnumerateArray().Select(t => t.GetProperty("nome").GetString()));
        await using var contexto = banco.CriarContexto();
        var registro = await contexto.Auditoria.AsNoTracking()
            .SingleAsync(r => r.RecursoIdPublico == idPublico && r.Operacao == "COLETOR_TENANTS_ALTERADOS");
        Assert.Equal("SuperAdmin", registro.Perfil);
        Assert.Contains(c.IdPublico.ToString(), registro.Detalhes);
    }

    [Fact]
    public async Task Versao_antiga_responde_409_CONFLITO_VERSAO()
    {
        var a = await Dados.NovoTenantAsync(banco);
        var b = await Dados.NovoTenantAsync(banco);
        var (idPublico, _) = await CriarAsync("Coletor Conflito", a);
        var (versaoLida, _, _) = await NoBancoAsync(idPublico);
        using (var primeira = await AlterarAsync(idPublico, new { tenants = new[] { b.IdPublico }, versao = versaoLida }))
            Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);

        using var resposta = await AlterarAsync(idPublico, new { tenants = new[] { a.IdPublico }, versao = versaoLida });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CONFLITO_VERSAO", await Sessao.CodigoAsync(resposta));
        var depois = await NoBancoAsync(idPublico);
        Assert.Equal([b.Id], depois.Tenants);
        Assert.Equal(2, depois.VersaoCredencial);
    }

    [Fact]
    public async Task Lista_vazia_responde_400_VALIDACAO()
    {
        var a = await Dados.NovoTenantAsync(banco);
        var (idPublico, _) = await CriarAsync("Coletor Vazio", a);
        var (versao, _, _) = await NoBancoAsync(idPublico);

        using var resposta = await AlterarAsync(idPublico, new { tenants = Array.Empty<Guid>(), versao });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", json.GetProperty("codigo").GetString());
        Assert.True(json.GetProperty("erros").TryGetProperty("tenants", out _));
        Assert.Equal([a.Id], (await NoBancoAsync(idPublico)).Tenants);
    }

    [Fact]
    public async Task Admin_responde_403_na_lista_e_na_troca_de_tenants()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant), tenant);
        var (idPublico, _) = await CriarAsync("Coletor Protegido", tenant);
        var (versao, _, _) = await NoBancoAsync(idPublico);

        using var lista = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, admin));
        using var troca = await AlterarAsync(idPublico, new { tenants = new[] { tenant.IdPublico }, versao }, admin);

        Assert.Equal(HttpStatusCode.Forbidden, lista.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(lista));
        Assert.Equal(HttpStatusCode.Forbidden, troca.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(troca));
        Assert.Equal(1, (await NoBancoAsync(idPublico)).VersaoCredencial);
    }
}
