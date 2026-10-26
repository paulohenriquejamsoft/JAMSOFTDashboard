using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Mcp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Mcp;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ChavesMcpEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/chaves-mcp";

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

    private async Task<(Tenant Tenant, string Token)> TenantComUsuarioAsync(Perfil perfil)
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Chaves MCP");
        var usuario = await Dados.NovoUsuarioAsync(banco, perfil, tenant);
        return (tenant, Acesso.Token(usuario, tenant));
    }

    private Task<HttpResponseMessage> CriarAsync(string token, object corpo) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token, corpo));

    [Fact]
    public async Task Admin_cria_chave_201_mostra_a_chave_uma_vez_grava_so_o_hash_e_audita()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);

        using var resposta = await CriarAsync(token, new { descricao = "  Claude - Painéis  ", expiraEm = "2099-12-31" });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        var chave = corpo.GetProperty("chave").GetString()!;
        Assert.True(FormatoChaveMcp.TentarLerPrefixo(chave, out var prefixo));
        var registro = corpo.GetProperty("registro");
        var idPublico = registro.GetProperty("idPublico").GetGuid();
        Assert.Equal(prefixo, registro.GetProperty("prefixo").GetString());
        Assert.Equal("Claude - Painéis", registro.GetProperty("descricao").GetString());
        Assert.Equal("2099-12-31", registro.GetProperty("expiraEm").GetString());
        Assert.True(registro.GetProperty("ativo").GetBoolean());

        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        var gravada = await contexto.ChavesMcp.AsNoTracking().SingleAsync(c => c.IdPublico == idPublico);
        Assert.Equal(tenant.Id, gravada.TenantId);
        Assert.Equal(FormatoChaveMcp.Hash(chave), gravada.Hash);
        var auditoria = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == idPublico);
        Assert.Equal("CHAVE_MCP_CRIADA", auditoria.Operacao);
        Assert.Equal(tenant.IdPublico, auditoria.TenantIdPublico);
        Assert.DoesNotContain(chave, auditoria.Detalhes);

        // A listagem nunca devolve a chave.
        using var lista = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token));
        var texto = await lista.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.Contains(prefixo, texto);
        Assert.DoesNotContain(chave, texto);
    }

    [Fact]
    public async Task Descricao_vazia_ou_validade_no_passado_responde_400_VALIDACAO()
    {
        var (_, token) = await TenantComUsuarioAsync(Perfil.Admin);

        using var resposta = await CriarAsync(token, new { descricao = " ", expiraEm = "2020-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty("descricao", out _));
        Assert.True(corpo.GetProperty("erros").TryGetProperty("expiraEm", out _));
    }

    [Fact]
    public async Task Revogar_inativa_audita_e_repetir_devolve_o_mesmo_registro()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);
        using var criada = await CriarAsync(token, new { descricao = "Claude" });
        var idPublico = (await Sessao.JsonAsync(criada)).GetProperty("registro").GetProperty("idPublico").GetGuid();

        using var primeira = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Post, $"{Rota}/{idPublico}/revogar", token));
        using var segunda = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Post, $"{Rota}/{idPublico}/revogar", token));

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.False((await Sessao.JsonAsync(segunda)).GetProperty("ativo").GetBoolean());
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        var gravada = await contexto.ChavesMcp.AsNoTracking().SingleAsync(c => c.IdPublico == idPublico);
        Assert.False(gravada.Ativo);
        Assert.NotNull(gravada.RevogadoEm);
        Assert.Equal(1, await contexto.Auditoria.CountAsync(a => a.RecursoIdPublico == idPublico && a.Operacao == "CHAVE_MCP_REVOGADA"));
    }

    [Fact]
    public async Task Consulta_nao_lista_nem_cria_chaves()
    {
        var (_, token) = await TenantComUsuarioAsync(Perfil.Consulta);

        using var lista = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token));
        using var criada = await CriarAsync(token, new { descricao = "Claude" });

        Assert.Equal(HttpStatusCode.Forbidden, lista.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, criada.StatusCode);
    }
}
