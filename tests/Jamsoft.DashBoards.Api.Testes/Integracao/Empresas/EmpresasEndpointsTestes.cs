using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Empresas;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class EmpresasEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/empresas";

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

    private async Task<(Tenant Tenant, string Token)> TenantComUsuarioAsync(Perfil perfil, string nome = "Cliente Empresas")
    {
        var tenant = await Dados.NovoTenantAsync(banco, nome);
        var usuario = await Dados.NovoUsuarioAsync(banco, perfil, tenant);
        return (tenant, Acesso.Token(usuario, tenant));
    }

    private async Task<Empresa> SemearEmpresaAsync(Tenant tenant, int codEmpresa, string nome)
    {
        await using var contexto = banco.CriarContexto();
        var empresa = new Empresa { TenantId = tenant.Id, CodEmpresa = codEmpresa, Nome = nome };
        contexto.Empresas.Add(empresa);
        await contexto.SaveChangesAsync();
        return empresa;
    }

    private Task<HttpResponseMessage> CriarAsync(string token, object corpo) =>
        _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token, corpo));

    private static List<string> Nomes(JsonElement lista) =>
        [.. lista.EnumerateArray().Select(e => e.GetProperty("nome").GetString()!)];

    [Fact]
    public async Task Admin_cria_empresa_201_vinculada_ao_tenant_com_auditoria()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);

        using var resposta = await CriarAsync(token, new { codEmpresa = 12, nome = "  Filial Norte  " });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        var idPublico = corpo.GetProperty("idPublico").GetGuid();
        Assert.Equal(12, corpo.GetProperty("codEmpresa").GetInt32());
        Assert.Equal("Filial Norte", corpo.GetProperty("nome").GetString());
        Assert.False(corpo.TryGetProperty("tenantId", out _));
        Assert.False(corpo.TryGetProperty("id", out _));

        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        var gravada = await contexto.Empresas.AsNoTracking().SingleAsync(e => e.IdPublico == idPublico);
        Assert.Equal(tenant.Id, gravada.TenantId);
        Assert.Equal(12, gravada.CodEmpresa);
        Assert.Equal("Filial Norte", gravada.Nome);
        var auditoria = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == idPublico);
        Assert.Equal("EMPRESA_CRIADA", auditoria.Operacao);
        Assert.Equal(tenant.IdPublico, auditoria.TenantIdPublico);
        Assert.Equal("Cliente Empresas", auditoria.TenantNome);
        Assert.Equal("Admin", auditoria.Perfil);
        Assert.Equal("Sucesso", auditoria.Resultado);
    }

    [Fact]
    public async Task Codigo_duplicado_no_tenant_responde_409_EMPRESA_DUPLICADA_e_em_outro_tenant_e_aceito()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var outro = await Dados.NovoTenantAsync(banco);
        await SemearEmpresaAsync(outro, 1, "Empresa 1 do outro tenant");

        using var primeira = await CriarAsync(token, new { codEmpresa = 1, nome = "Matriz" });
        using var segunda = await CriarAsync(token, new { codEmpresa = 1, nome = "Matriz de novo" });

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("EMPRESA_DUPLICADA", await Sessao.CodigoAsync(segunda));
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        Assert.Equal(["Matriz"], await contexto.Empresas.Select(e => e.Nome).ToListAsync());
    }

    [Fact]
    public async Task Codigo_zero_ou_nome_com_81_caracteres_responde_400_VALIDACAO()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);

        using var codigoZero = await CriarAsync(token, new { codEmpresa = 0, nome = "Filial" });
        using var nomeLongo = await CriarAsync(token, new { codEmpresa = 2, nome = new string('x', 81) });

        Assert.Equal(HttpStatusCode.BadRequest, codigoZero.StatusCode);
        var erroCodigo = await Sessao.JsonAsync(codigoZero);
        Assert.Equal("VALIDACAO", erroCodigo.GetProperty("codigo").GetString());
        Assert.True(erroCodigo.GetProperty("erros").TryGetProperty("codEmpresa", out _));
        Assert.Equal(HttpStatusCode.BadRequest, nomeLongo.StatusCode);
        var erroNome = await Sessao.JsonAsync(nomeLongo);
        Assert.Equal("VALIDACAO", erroNome.GetProperty("codigo").GetString());
        Assert.True(erroNome.GetProperty("erros").TryGetProperty("nome", out _));
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        Assert.Equal(0, await contexto.Empresas.CountAsync());
    }

    [Fact]
    public async Task SuperAdmin_com_X_Tenant_cria_empresa_201_no_tenant_escolhido()
    {
        var escolhido = await Dados.NovoTenantAsync(banco, "Cliente Escolhido Empresas");
        var outro = await Dados.NovoTenantAsync(banco);
        var token = await Acesso.TokenSuperAdminAsync(banco);

        using var resposta = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, token,
            new { codEmpresa = 7, nome = "Filial do SuperAdmin" }, escolhido.IdPublico));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var idPublico = (await Sessao.JsonAsync(resposta)).GetProperty("idPublico").GetGuid();
        await using var contexto = banco.CriarContexto(new TenantFixo(escolhido.Id));
        var gravada = await contexto.Empresas.AsNoTracking().SingleAsync(e => e.IdPublico == idPublico);
        Assert.Equal(escolhido.Id, gravada.TenantId);
        Assert.Equal(7, gravada.CodEmpresa);
        Assert.Equal("Filial do SuperAdmin", gravada.Nome);
        await using var doOutro = banco.CriarContexto(new TenantFixo(outro.Id));
        Assert.Equal(0, await doOutro.Empresas.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nome_vazio_responde_400_VALIDACAO(string nome)
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);

        using var resposta = await CriarAsync(token, new { codEmpresa = 5, nome });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty("nome", out _));
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        Assert.Equal(0, await contexto.Empresas.CountAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    public async Task Nome_de_1_ou_80_caracteres_e_aceito(int tamanho)
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var nome = new string('n', tamanho);

        using var resposta = await CriarAsync(token, new { codEmpresa = 6, nome });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        Assert.Equal([nome], await contexto.Empresas.Select(e => e.Nome).ToListAsync());
    }

    [Fact]
    public async Task Consulta_criando_empresa_responde_403()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Consulta);

        using var resposta = await CriarAsync(token, new { codEmpresa = 3, nome = "Filial" });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        Assert.Equal(0, await contexto.Empresas.CountAsync());
    }

    [Fact]
    public async Task Consulta_lista_somente_as_empresas_do_proprio_tenant()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Consulta);
        var outro = await Dados.NovoTenantAsync(banco);
        await SemearEmpresaAsync(tenant, 2, "Própria 2");
        await SemearEmpresaAsync(tenant, 1, "Própria 1");
        await SemearEmpresaAsync(outro, 1, "Do outro tenant");

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(["Própria 1", "Própria 2"], Nomes(await Sessao.JsonAsync(resposta)));
    }

    [Fact]
    public async Task SuperAdmin_com_X_Tenant_lista_as_empresas_do_tenant_escolhido()
    {
        var escolhido = await Dados.NovoTenantAsync(banco);
        var outro = await Dados.NovoTenantAsync(banco);
        await SemearEmpresaAsync(escolhido, 1, "Do escolhido");
        await SemearEmpresaAsync(outro, 1, "Do outro");
        var token = await Acesso.TokenSuperAdminAsync(banco);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token, escolhido.IdPublico));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(["Do escolhido"], Nomes(await Sessao.JsonAsync(resposta)));
    }

    [Fact]
    public async Task Renomear_marca_nome_manual_e_registra_auditoria()
    {
        var (tenant, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var empresa = await SemearEmpresaAsync(tenant, 4, "Nome do ERP");

        using var resposta = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, $"{Rota}/{empresa.IdPublico}", token,
            new { nome = "Nome do Admin" }));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("Nome do Admin", corpo.GetProperty("nome").GetString());
        Assert.True(corpo.GetProperty("nomeManual").GetBoolean());
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        var gravada = await contexto.Empresas.AsNoTracking().SingleAsync(e => e.Id == empresa.Id);
        Assert.Equal("Nome do Admin", gravada.Nome);
        Assert.True(gravada.NomeManual);
        var auditoria = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == empresa.IdPublico);
        Assert.Equal("EMPRESA_ALTERADA", auditoria.Operacao);
        Assert.Equal(tenant.IdPublico, auditoria.TenantIdPublico);
    }

    [Fact]
    public async Task Renomear_empresa_de_outro_tenant_responde_404_sem_alterar()
    {
        var (_, token) = await TenantComUsuarioAsync(Perfil.Admin);
        var outro = await Dados.NovoTenantAsync(banco);
        var empresa = await SemearEmpresaAsync(outro, 1, "Do outro tenant");

        using var resposta = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, $"{Rota}/{empresa.IdPublico}", token,
            new { nome = "Invadida" }));

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("NAO_ENCONTRADO", await Sessao.CodigoAsync(resposta));
        await using var contexto = banco.CriarContexto(new TenantFixo(outro.Id));
        var gravada = await contexto.Empresas.AsNoTracking().SingleAsync(e => e.Id == empresa.Id);
        Assert.Equal("Do outro tenant", gravada.Nome);
        Assert.False(gravada.NomeManual);
    }
}
