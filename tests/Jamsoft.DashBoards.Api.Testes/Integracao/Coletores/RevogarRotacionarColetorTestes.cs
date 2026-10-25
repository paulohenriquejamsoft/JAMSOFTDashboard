using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Coletores;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class RevogarRotacionarColetorTestes(BancoDeTestes banco) : IAsyncLifetime
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

    private async Task<(Guid IdPublico, string Segredo)> CriarAsync(string nome)
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        using var resposta = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Post, Rota, _token,
            new { nome, tenants = new[] { tenant.IdPublico } }));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        return (json.GetProperty("idPublico").GetGuid(), json.GetProperty("clientSecret").GetString()!);
    }

    private Task<HttpResponseMessage> AcaoAsync(Guid idPublico, string acao) =>
        _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Post, $"{Rota}/{idPublico}/{acao}", _token));

    private async Task<Coletor> NoBancoAsync(Guid idPublico)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Coletores.AsNoTracking().SingleAsync(c => c.IdPublico == idPublico);
    }

    [Fact]
    public async Task Revogar_deixa_a_credencial_inativa_e_incrementa_a_versao()
    {
        var (idPublico, _) = await CriarAsync("Coletor Revogado");
        var antes = await NoBancoAsync(idPublico);

        using var resposta = await AcaoAsync(idPublico, "revogar");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False((await Sessao.JsonAsync(resposta)).GetProperty("ativo").GetBoolean());
        var depois = await NoBancoAsync(idPublico);
        Assert.False(depois.Ativo);
        Assert.NotNull(depois.RevogadoEm);
        Assert.Equal(antes.VersaoCredencial + 1, depois.VersaoCredencial);
        Assert.Equal(antes.SecretHash, depois.SecretHash);
    }

    [Fact]
    public async Task Rotacionar_devolve_secret_novo_troca_o_hash_e_incrementa_a_versao()
    {
        var (idPublico, segredoAntigo) = await CriarAsync("Coletor Rotacionado");
        var antes = await NoBancoAsync(idPublico);

        using var resposta = await AcaoAsync(idPublico, "rotacionar");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await Sessao.JsonAsync(resposta);
        var segredoNovo = json.GetProperty("clientSecret").GetString()!;
        Assert.Equal(43, segredoNovo.Length);
        Assert.NotEqual(segredoAntigo, segredoNovo);
        Assert.Equal(antes.ClientId, json.GetProperty("clientId").GetGuid());
        var depois = await NoBancoAsync(idPublico);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(segredoNovo)), depois.SecretHash);
        Assert.NotEqual(antes.SecretHash, depois.SecretHash);
        Assert.Equal(antes.VersaoCredencial + 1, depois.VersaoCredencial);
        Assert.True(depois.Ativo);
    }

    [Fact]
    public async Task Rotacionar_coletor_revogado_responde_409_e_nao_troca_o_hash()
    {
        var (idPublico, _) = await CriarAsync("Coletor Revogado Rotacao");
        using (var revogar = await AcaoAsync(idPublico, "revogar")) Assert.Equal(HttpStatusCode.OK, revogar.StatusCode);
        var antes = await NoBancoAsync(idPublico);

        using var resposta = await AcaoAsync(idPublico, "rotacionar");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("COLETOR_REVOGADO", await Sessao.CodigoAsync(resposta));
        var depois = await NoBancoAsync(idPublico);
        Assert.Equal(antes.SecretHash, depois.SecretHash);
        Assert.Equal(antes.VersaoCredencial, depois.VersaoCredencial);
    }

    [Fact]
    public async Task Auditoria_registra_rotacao_e_revogacao_sem_segredo()
    {
        var (idPublico, segredoAntigo) = await CriarAsync("Coletor Auditoria");
        string segredoNovo;
        using (var rotacionar = await AcaoAsync(idPublico, "rotacionar"))
            segredoNovo = (await Sessao.JsonAsync(rotacionar)).GetProperty("clientSecret").GetString()!;
        using (await AcaoAsync(idPublico, "revogar")) { }

        await using var contexto = banco.CriarContexto();
        var registros = await contexto.Auditoria.AsNoTracking()
            .Where(r => r.RecursoIdPublico == idPublico && r.Operacao != "COLETOR_CRIADO")
            .OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(["COLETOR_SECRET_ROTACIONADO", "COLETOR_REVOGADO"], registros.Select(r => r.Operacao));
        Assert.All(registros, r =>
        {
            Assert.Equal("SuperAdmin", r.Perfil);
            Assert.Equal("Sucesso", r.Resultado);
            Assert.DoesNotContain(segredoAntigo, r.Detalhes);
            Assert.DoesNotContain(segredoNovo, r.Detalhes);
            Assert.DoesNotContain("secret", r.Detalhes, StringComparison.OrdinalIgnoreCase);
        });
    }
}
