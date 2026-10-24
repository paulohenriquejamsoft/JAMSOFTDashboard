using System.Net;
using System.Net.Http.Json;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;

namespace Jamsoft.DashBoards.Api.Testes.Integracao;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class HealthTestes(BancoDeTestes banco)
{
    [Fact]
    public async Task Health_sem_autenticacao_responde_ok()
    {
        await using var api = new ApiFactory(banco);
        using var cliente = api.CreateClient();

        var resposta = await cliente.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("ok", corpo!["status"]);
    }
}
