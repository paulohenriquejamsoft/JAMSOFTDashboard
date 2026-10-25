using System.Net;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Tenants;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class CriarTenantTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/admin/tenants";
    private const string SenhaInicial = "SenhaInicial2026";

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

    private async Task<int> TenantsComCnpjAsync(string cnpj)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Tenants.CountAsync(t => t.Cnpj == cnpj);
    }

    [Fact]
    public async Task Criacao_simples_responde_201_com_idPublico_sem_ID_interno_e_audita()
    {
        var cnpj = Dados.CnpjValido();
        var mascarado = $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

        using var resposta = await CriarAsync(new { nome = "  Mercado Central  ", cnpj = mascarado });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        var idPublico = corpo.GetProperty("idPublico").GetGuid();
        Assert.NotEqual(Guid.Empty, idPublico);
        Assert.False(corpo.TryGetProperty("id", out _));
        Assert.Equal("Mercado Central", corpo.GetProperty("nome").GetString());
        Assert.Equal(cnpj, corpo.GetProperty("cnpj").GetString());
        Assert.True(corpo.GetProperty("ativo").GetBoolean());
        Assert.Equal($"/api/admin/tenants/{idPublico}", resposta.Headers.Location?.OriginalString);

        await using var contexto = banco.CriarContexto();
        var gravado = await contexto.Tenants.AsNoTracking().SingleAsync(t => t.IdPublico == idPublico);
        Assert.Equal(cnpj, gravado.Cnpj);
        Assert.True(gravado.Ativo);
        Assert.False(await contexto.Usuarios.AnyAsync(u => u.TenantId == gravado.Id));
        var registro = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == idPublico);
        Assert.Equal("TENANT_CRIADO", registro.Operacao);
        Assert.Equal(idPublico, registro.TenantIdPublico);
        Assert.Equal("Mercado Central", registro.TenantNome);
        Assert.Equal("SuperAdmin", registro.Perfil);
        Assert.Equal("Sucesso", registro.Resultado);
    }

    [Fact]
    public async Task Com_Admin_inicial_cria_Admin_do_tenant_com_troca_de_senha_pendente()
    {
        var email = $"{Guid.NewGuid():N}@cliente.com";

        using var resposta = await CriarAsync(new
        {
            nome = "Mercado com Admin",
            cnpj = Dados.CnpjValido(),
            adminInicial = new { nome = "Maria Admin", email = email.ToUpperInvariant(), senhaInicial = SenhaInicial },
        });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var idPublico = (await Sessao.JsonAsync(resposta)).GetProperty("idPublico").GetGuid();
        await using (var contexto = banco.CriarContexto())
        {
            var tenant = await contexto.Tenants.AsNoTracking().SingleAsync(t => t.IdPublico == idPublico);
            var admin = await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.TenantId == tenant.Id);
            Assert.Equal(email, admin.Email);
            Assert.Equal("Maria Admin", admin.Nome);
            Assert.Equal(Perfil.Admin, admin.Perfil);
            Assert.True(admin.Ativo);
            Assert.True(admin.TrocarSenha);
            var registro = await contexto.Auditoria.AsNoTracking().SingleAsync(a => a.RecursoIdPublico == idPublico);
            Assert.DoesNotContain(SenhaInicial, registro.Detalhes ?? "");
        }

        // A senha inicial vale e o login pede a troca.
        using var login = await Sessao.LoginAsync(_cliente, email, SenhaInicial);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await Sessao.JsonAsync(login)).GetProperty("trocarSenha").GetBoolean());
    }

    [Fact]
    public async Task CNPJ_duplicado_responde_409_CNPJ_DUPLICADO_sem_criar_registro()
    {
        var cnpj = Dados.CnpjValido();
        using var primeira = await CriarAsync(new { nome = "Primeiro", cnpj });
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

        using var segunda = await CriarAsync(new { nome = "Segundo", cnpj });

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("CNPJ_DUPLICADO", await Sessao.CodigoAsync(segunda));
        Assert.Equal(1, await TenantsComCnpjAsync(cnpj));
    }

    [Fact]
    public async Task Email_do_Admin_duplicado_responde_409_EMAIL_EM_USO_e_desfaz_o_tenant()
    {
        var existente = await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, await Dados.NovoTenantAsync(banco));
        var cnpj = Dados.CnpjValido();
        var nome = $"Tenant que não fica {Guid.NewGuid():N}";

        using var resposta = await CriarAsync(new
        {
            nome,
            cnpj,
            adminInicial = new { nome = "Outro", email = existente.Email, senhaInicial = SenhaInicial },
        });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("EMAIL_EM_USO", await Sessao.CodigoAsync(resposta));
        Assert.Equal(0, await TenantsComCnpjAsync(cnpj));
        await using var contexto = banco.CriarContexto();
        Assert.False(await contexto.Auditoria.AnyAsync(a => a.TenantNome == nome));
    }

    [Theory]
    [InlineData("AB", null, "nome")]
    [InlineData("Nome válido", "12345678000100", "cnpj")]
    public async Task Nome_curto_ou_CNPJ_invalido_responde_400_VALIDACAO(string nome, string? cnpj, string campo)
    {
        cnpj ??= Dados.CnpjValido();

        using var resposta = await CriarAsync(new { nome, cnpj });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty(campo, out _));
        Assert.Equal(0, await TenantsComCnpjAsync(cnpj));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(120)]
    public async Task Nome_de_3_ou_120_caracteres_responde_201(int tamanho)
    {
        var cnpj = Dados.CnpjValido();
        var nome = new string('t', tamanho);

        using var resposta = await CriarAsync(new { nome, cnpj });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(nome, (await Sessao.JsonAsync(resposta)).GetProperty("nome").GetString());
        Assert.Equal(1, await TenantsComCnpjAsync(cnpj));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(121)]
    public async Task Nome_de_2_ou_121_caracteres_responde_400_VALIDACAO(int tamanho)
    {
        var cnpj = Dados.CnpjValido();

        using var resposta = await CriarAsync(new { nome = new string('t', tamanho), cnpj });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await Sessao.JsonAsync(resposta);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.True(corpo.GetProperty("erros").TryGetProperty("nome", out _));
        Assert.Equal(0, await TenantsComCnpjAsync(cnpj));
    }

    [Fact]
    public async Task Admin_chamando_responde_403_ACESSO_NEGADO()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        var cnpj = Dados.CnpjValido();

        using var resposta = await CriarAsync(new { nome = "Tentativa de Admin", cnpj }, Acesso.Token(admin, tenant));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
        Assert.Equal(0, await TenantsComCnpjAsync(cnpj));
    }

    [Fact]
    public async Task Requisicoes_simultaneas_com_o_mesmo_CNPJ_resultam_em_uma_201_e_uma_409()
    {
        var cnpj = Dados.CnpjValido();

        var respostas = await Task.WhenAll(
            CriarAsync(new { nome = "Concorrente A", cnpj }),
            CriarAsync(new { nome = "Concorrente B", cnpj }));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], respostas.Select(r => r.StatusCode).Order());
        Assert.Equal("CNPJ_DUPLICADO",
            await Sessao.CodigoAsync(respostas.Single(r => r.StatusCode == HttpStatusCode.Conflict)));
        Assert.Equal(1, await TenantsComCnpjAsync(cnpj));
        foreach (var r in respostas) r.Dispose();
    }
}
