using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Erros;

[Trait("Categoria", "Integracao")]
public sealed class ProblemasApiTestes
{
    private const string MensagemInterna = "detalhe interno que nao pode vazar";

    private static Task<HostDeTeste> IniciarAsync() => HostDeTeste.IniciarAsync(
        s => s.AddErrosApi(),
        app =>
        {
            app.UseErrosApi();
            app.MapGet("/teste/excecao", string () => throw new InvalidOperationException(MensagemInterna));
            app.MapPost("/teste/validacao", () => ProblemasApi.Validacao(new Dictionary<string, string[]>
            {
                ["nome"] = ["O nome é obrigatório."],
                ["cnpj"] = ["CNPJ inválido."],
            }));
        });

    [Fact]
    public async Task Excecao_nao_tratada_vira_500_ERRO_INTERNO_sem_detalhes_internos()
    {
        await using var host = await IniciarAsync();

        var resposta = await host.Cliente.GetAsync("/teste/excecao");
        var texto = await resposta.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(texto);
        var corpo = json.RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("ERRO_INTERNO", corpo.GetProperty("codigo").GetString());
        Assert.Equal(500, corpo.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(MensagemInterna, texto);
        Assert.DoesNotContain(nameof(InvalidOperationException), texto);
        Assert.DoesNotContain("   at ", texto);
    }

    [Fact]
    public async Task Validacao_vira_400_VALIDACAO_com_erros_por_campo()
    {
        await using var host = await IniciarAsync();

        var resposta = await host.Cliente.PostAsync("/teste/validacao", null);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var corpo = json.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("VALIDACAO", corpo.GetProperty("codigo").GetString());
        Assert.Equal("O nome é obrigatório.", corpo.GetProperty("erros").GetProperty("nome")[0].GetString());
        Assert.Equal("CNPJ inválido.", corpo.GetProperty("erros").GetProperty("cnpj")[0].GetString());
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("traceId").GetString()));
    }
}
