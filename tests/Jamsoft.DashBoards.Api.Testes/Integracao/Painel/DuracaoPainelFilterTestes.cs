using System.Net;
using System.Text.RegularExpressions;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Painel;

/// <summary>Log de duração por bloco do painel (observabilidade da meta de 2 s), sem dados de venda.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class DuracaoPainelFilterTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private static readonly string[] Blocos = ["kpis", "evolucao", "rankings", "dfe"];

    private readonly LogsCapturados _logs = new();
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco).WithWebHostBuilder(b => b.ConfigureLogging(l =>
        {
            l.SetMinimumLevel(LogLevel.Trace);
            l.AddProvider(_logs);
        }));
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    /// <summary>Chama os 4 blocos no período semeado e devolve as linhas de log do filtro com o tenant.</summary>
    private async Task<List<string>> ChamarBlocosAsync(ConjuntoVendas c)
    {
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, c.Tenant);
        var token = Acesso.Token(usuario, c.Tenant);
        foreach (var bloco in Blocos)
        {
            using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get,
                $"/api/painel/{bloco}?inicio=2026-10-11&fim=2026-10-20&empresas={c.Matriz.IdPublico}&empresas={c.Filial.IdPublico}",
                token));
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }
        return _logs.Linhas.Where(l => l.Contains("DuracaoPainelFilter") && l.Contains(c.Tenant.IdPublico.ToString())).ToList();
    }

    [Fact]
    public async Task Cada_bloco_gera_um_log_com_o_nome_do_bloco_e_a_duracao()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Duração"));

        var linhas = await ChamarBlocosAsync(c);

        Assert.Equal(4, linhas.Count);
        foreach (var bloco in Blocos)
        {
            var linha = Assert.Single(linhas, l => Regex.IsMatch(l, $@"Painel {bloco} \d+ ms"));
            Assert.Matches($@"Bloco={bloco} DuracaoMs=\d+ ", linha);
            Assert.Contains("Dias=10", linha);
        }
    }

    [Fact]
    public async Task Log_nao_contem_valores_nem_nomes_de_clientes()
    {
        var c = await VendasSemeadas.SemearAsync(banco, await Dados.NovoTenantAsync(banco, "Cliente Duração Sigilo"));

        var linhas = await ChamarBlocosAsync(c);

        Assert.Equal(4, linhas.Count);
        // Valores com casas decimais: a duração é inteira e o GUID do tenant não tem ponto nem vírgula.
        string[] proibidos = ["Cliente X", "Cliente Y", "Ana", "Bruno", "Matriz", "Filial",
            "375.5", "375,5", "225.5", "225,5", "93.8", "93,8", "25.5", "25,5", "100.0", "100,0",
            c.Matriz.IdPublico.ToString(), c.Filial.IdPublico.ToString()];
        foreach (var linha in linhas)
            foreach (var proibido in proibidos)
                Assert.DoesNotContain(proibido, linha);
    }
}
