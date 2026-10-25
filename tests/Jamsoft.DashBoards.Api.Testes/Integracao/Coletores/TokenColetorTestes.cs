using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Coletores;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class TokenColetorTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/coletor/token";

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

    private Task<HttpResponseMessage> TokenAsync(Guid clientId, string clientSecret) =>
        _cliente.PostAsJsonAsync(Rota, new { clientId, clientSecret });

    private async Task RevogarNoBancoAsync(Coletor coletor)
    {
        await using var contexto = banco.CriarContexto();
        await contexto.Coletores.Where(c => c.Id == coletor.Id)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.Ativo, false).SetProperty(x => x.RevogadoEm, DateTime.UtcNow));
    }

    /// <summary>Corpo da recusa sem o <c>traceId</c>, que muda a cada requisição.</summary>
    private static async Task<(HttpStatusCode Status, string Corpo)> RecusaAsync(HttpResponseMessage resposta)
    {
        var json = await Sessao.JsonAsync(resposta);
        var campos = json.EnumerateObject().Where(p => p.Name != "traceId")
            .Select(p => $"{p.Name}={p.Value.GetRawText()}");
        return (resposta.StatusCode, string.Join("|", campos));
    }

    [Fact]
    public async Task Credencial_valida_responde_200_com_token_de_Coletor_dos_tenants_autorizados_e_ativos()
    {
        var a = await Dados.NovoTenantAsync(banco, "Tenant Ativo A");
        var b = await Dados.NovoTenantAsync(banco, "Tenant Ativo B");
        var inativo = await Dados.NovoTenantAsync(banco, "Tenant Inativo", ativo: false);
        var (coletor, segredo) = await Dados.NovoColetorComSegredoAsync(banco, b, a, inativo);
        // Outro coletor com outro tenant não interfere.
        await Dados.NovoColetorComSegredoAsync(banco, await Dados.NovoTenantAsync(banco));
        await using (var contexto = banco.CriarContexto())
            await contexto.Coletores.Where(c => c.Id == coletor.Id)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.VersaoCredencial, 7));

        using var resposta = await TokenAsync(coletor.ClientId, segredo);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(json.GetProperty("accessToken").GetString());
        Assert.Equal("Coletor", jwt.GetClaim("perfil").Value);
        Assert.Equal(coletor.IdPublico.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal(new[] { a.IdPublico.ToString(), b.IdPublico.ToString() }.Order(),
            jwt.Claims.Where(c => c.Type == "tenants").Select(c => c.Value).Order());
        Assert.Equal("7", jwt.GetClaim("cred_ver").Value);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
        // exp do JWT tem precisão de segundos; expiraEm mantém a fração.
        var expiraEm = json.GetProperty("expiraEm").GetDateTimeOffset().UtcDateTime;
        Assert.Equal(jwt.ValidTo, expiraEm.AddTicks(-(expiraEm.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal(
            [(a.IdPublico, "Tenant Ativo A"), (b.IdPublico, "Tenant Ativo B")],
            json.GetProperty("tenants").EnumerateArray()
                .Select(t => (t.GetProperty("idPublico").GetGuid(), t.GetProperty("nome").GetString())));
        await using var leitura = banco.CriarContexto();
        Assert.NotNull((await leitura.Coletores.AsNoTracking().Include(c => c.Acesso).SingleAsync(c => c.Id == coletor.Id)).Acesso?.UltimoTokenEm);
    }

    [Fact]
    public async Task Secret_errado_responde_401_CREDENCIAL_INVALIDA()
    {
        var (coletor, segredo) = await Dados.NovoColetorComSegredoAsync(banco, await Dados.NovoTenantAsync(banco));
        var errado = (segredo[0] == 'A' ? 'B' : 'A') + segredo[1..];

        using var resposta = await TokenAsync(coletor.ClientId, errado);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("CREDENCIAL_INVALIDA", await Sessao.CodigoAsync(resposta));
    }

    [Fact]
    public async Task ClientId_inexistente_responde_401_CREDENCIAL_INVALIDA()
    {
        using var resposta = await TokenAsync(Guid.NewGuid(), SegredoColetor.Gerar());

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("CREDENCIAL_INVALIDA", await Sessao.CodigoAsync(resposta));
    }

    [Fact]
    public async Task Credencial_revogada_responde_401_CREDENCIAL_INVALIDA_mesmo_com_o_secret_certo()
    {
        var (coletor, segredo) = await Dados.NovoColetorComSegredoAsync(banco, await Dados.NovoTenantAsync(banco));
        await RevogarNoBancoAsync(coletor);

        using var resposta = await TokenAsync(coletor.ClientId, segredo);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("CREDENCIAL_INVALIDA", await Sessao.CodigoAsync(resposta));
    }

    [Fact]
    public async Task Secret_errado_clientId_inexistente_e_revogada_tem_corpo_identico()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var (ativo, segredoAtivo) = await Dados.NovoColetorComSegredoAsync(banco, tenant);
        var (revogado, segredoRevogado) = await Dados.NovoColetorComSegredoAsync(banco, tenant);
        await RevogarNoBancoAsync(revogado);

        using var errado = await TokenAsync(ativo.ClientId, segredoAtivo + "x");
        using var inexistente = await TokenAsync(Guid.NewGuid(), segredoAtivo);
        using var revogada = await TokenAsync(revogado.ClientId, segredoRevogado);

        var recusas = new[] { await RecusaAsync(errado), await RecusaAsync(inexistente), await RecusaAsync(revogada) };
        Assert.All(recusas, r => Assert.Equal(HttpStatusCode.Unauthorized, r.Status));
        Assert.Single(recusas.Select(r => r.Corpo).Distinct());
        Assert.Contains("codigo=\"CREDENCIAL_INVALIDA\"", recusas[0].Corpo);
    }

    [Fact]
    public async Task Vigesima_primeira_requisicao_no_minuto_responde_429()
    {
        for (var i = 1; i <= 20; i++)
        {
            using var permitida = await TokenAsync(Guid.NewGuid(), "qualquer");
            Assert.Equal(HttpStatusCode.Unauthorized, permitida.StatusCode);
        }

        using var resposta = await TokenAsync(Guid.NewGuid(), "qualquer");

        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        Assert.Equal("MUITAS_REQUISICOES", await Sessao.CodigoAsync(resposta));
    }

    [Fact]
    public async Task Tenants_do_coletor_retorna_os_autorizados_e_ativos_ordenados_por_nome()
    {
        var zeta = await Dados.NovoTenantAsync(banco, "Zeta Coletor");
        var alfa = await Dados.NovoTenantAsync(banco, "Alfa Coletor");
        var inativado = await Dados.NovoTenantAsync(banco, "Beta Inativado");
        await Dados.NovoTenantAsync(banco, "Aaa Nao Autorizado");
        var (coletor, segredo) = await Dados.NovoColetorComSegredoAsync(banco, zeta, inativado, alfa);
        string token;
        using (var emissao = await TokenAsync(coletor.ClientId, segredo))
            token = (await Sessao.JsonAsync(emissao)).GetProperty("accessToken").GetString()!;
        // Inativado depois da emissão: continua na claim, mas não volta na lista.
        await using (var contexto = banco.CriarContexto())
            await contexto.Tenants.Where(t => t.Id == inativado.Id).ExecuteUpdateAsync(t => t.SetProperty(x => x.Ativo, false));

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/coletor/tenants", token));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        Assert.Equal(
            [(alfa.IdPublico, "Alfa Coletor"), (zeta.IdPublico, "Zeta Coletor")],
            json.EnumerateArray().Select(t => (t.GetProperty("idPublico").GetGuid(), t.GetProperty("nome").GetString())));
    }

    [Fact]
    public async Task Tenants_do_coletor_com_token_de_usuario_responde_403()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = Acesso.Token(await Dados.NovoUsuarioAsync(banco, Api.Seguranca.Perfil.Admin, tenant), tenant);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, "/api/coletor/tenants", admin));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
    }
}
