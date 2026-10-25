using System.Net;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jamsoft.DashBoards.Api.Testes.Integracao;

/// <summary>
/// Suíte de isolamento (ISO-02, ISO-03, ISO-05): para cada rota tenant-scoped (metadado <see cref="RotaTenantScoped"/>,
/// descoberta pelo <see cref="EndpointDataSource"/>), Admin e Consulta do tenant A tentam ler, alterar e excluir dados
/// do tenant B. Pelo ID público de B → 404; pelo cabeçalho <c>X-Tenant</c> de B → 403; Consulta em rota de Admin → 403.
/// As linhas de B ficam iguais (contagem e checksum por tabela). Rota tenant-scoped nova sem caso em <see cref="Casos"/>
/// faz <see cref="Toda_rota_tenant_scoped_tem_caso_cadastrado"/> falhar.
/// </summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class IsolamentoTenantsTestes(BancoDeTestes banco) : IAsyncLifetime
{
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

    /// <summary>Recursos do tenant B que as tentativas miram.</summary>
    private sealed record TenantB(Tenant Tenant, Empresa Empresa, Usuario Usuario);

    /// <summary>Uma tentativa de acesso a B; <c>ViaCabecalho</c> quando usa <c>X-Tenant</c> de B.</summary>
    private sealed record Tentativa(HttpRequestMessage Requisicao, bool ViaCabecalho);

    /// <summary>Como atacar B por rota. <c>SomenteAdmin</c>: rota com policy AdminTenant (Consulta recebe 403).</summary>
    private sealed record Caso(bool SomenteAdmin, Func<TenantB, string, IEnumerable<Tentativa>> Tentativas);

    private static Tentativa PeloId(HttpMethod metodo, string rota, string token, object? corpo = null) =>
        new(Montar(metodo, rota, token, null, corpo), ViaCabecalho: false);

    private static Tentativa PeloCabecalho(HttpMethod metodo, string rota, string token, TenantB b, object? corpo = null) =>
        new(Montar(metodo, rota, token, b.Tenant.IdPublico, corpo), ViaCabecalho: true);

    private static HttpRequestMessage Montar(HttpMethod metodo, string rota, string token, Guid? xTenant, object? corpo)
    {
        var requisicao = Acesso.Requisicao(metodo, rota, token, xTenant);
        if (corpo is not null) requisicao.Content = System.Net.Http.Json.JsonContent.Create(corpo);
        return requisicao;
    }

    /// <summary>Chave: "MÉTODO /rota" como no <see cref="EndpointDataSource"/>.</summary>
    private static readonly Dictionary<string, Caso> Casos = new()
    {
        ["GET /api/empresas"] = new(false, (b, t) =>
            [PeloCabecalho(HttpMethod.Get, "/api/empresas", t, b)]),
        ["POST /api/empresas"] = new(true, (b, t) =>
            [PeloCabecalho(HttpMethod.Post, "/api/empresas", t, b, new { codEmpresa = 999, nome = "Invasora" })]),
        ["PUT /api/empresas/{idPublico}"] = new(true, (b, t) =>
        [
            PeloId(HttpMethod.Put, $"/api/empresas/{b.Empresa.IdPublico}", t, new { nome = "Renomeada" }),
            PeloCabecalho(HttpMethod.Put, $"/api/empresas/{b.Empresa.IdPublico}", t, b, new { nome = "Renomeada" }),
        ]),
        ["GET /api/painel/dfe"] = new(false, (b, t) =>
        [
            PeloId(HttpMethod.Get, $"/api/painel/dfe?empresas={b.Empresa.IdPublico}", t),
            PeloCabecalho(HttpMethod.Get, "/api/painel/dfe", t, b),
        ]),
        ["GET /api/painel/evolucao"] = new(false, (b, t) =>
        [
            PeloId(HttpMethod.Get, $"/api/painel/evolucao?empresas={b.Empresa.IdPublico}", t),
            PeloCabecalho(HttpMethod.Get, "/api/painel/evolucao", t, b),
        ]),
        ["GET /api/painel/rankings"] = new(false, (b, t) =>
        [
            PeloId(HttpMethod.Get, $"/api/painel/rankings?empresas={b.Empresa.IdPublico}", t),
            PeloCabecalho(HttpMethod.Get, "/api/painel/rankings", t, b),
        ]),
        ["GET /api/painel/kpis"] = new(false, (b, t) =>
        [
            PeloId(HttpMethod.Get, $"/api/painel/kpis?empresas={b.Empresa.IdPublico}", t),
            PeloCabecalho(HttpMethod.Get, "/api/painel/kpis", t, b),
        ]),
        ["GET /api/sincronizacao"] = new(true, (b, t) =>
            [PeloCabecalho(HttpMethod.Get, "/api/sincronizacao", t, b)]),
        ["GET /api/sincronizacao/resumo"] = new(false, (b, t) =>
            [PeloCabecalho(HttpMethod.Get, "/api/sincronizacao/resumo", t, b)]),
        ["GET /api/usuarios"] = new(true, (b, t) =>
            [PeloCabecalho(HttpMethod.Get, "/api/usuarios", t, b)]),
        ["POST /api/usuarios"] = new(true, (b, t) =>
        [
            PeloCabecalho(HttpMethod.Post, "/api/usuarios", t, b,
                new { nome = "Invasor", email = $"{Guid.NewGuid():N}@teste.com", senhaInicial = "SenhaNova12345", perfil = "Admin" }),
        ]),
        ["PUT /api/usuarios/{idPublico}"] = new(true, (b, t) =>
        {
            // Versão correta: só o isolamento pode barrar a inativação.
            var corpo = new { nome = "Alterado", perfil = "Consulta", ativo = false, versao = Convert.ToBase64String(b.Usuario.Versao) };
            return
            [
                PeloId(HttpMethod.Put, $"/api/usuarios/{b.Usuario.IdPublico}", t, corpo),
                PeloCabecalho(HttpMethod.Put, $"/api/usuarios/{b.Usuario.IdPublico}", t, b, corpo),
            ];
        }),
        ["POST /api/usuarios/{idPublico}/redefinir-senha"] = new(true, (b, t) =>
        [
            PeloId(HttpMethod.Post, $"/api/usuarios/{b.Usuario.IdPublico}/redefinir-senha", t,
                new { senhaTemporaria = "SenhaNova12345" }),
            PeloCabecalho(HttpMethod.Post, $"/api/usuarios/{b.Usuario.IdPublico}/redefinir-senha", t, b,
                new { senhaTemporaria = "SenhaNova12345" }),
        ]),
    };

    public static TheoryData<string, string> RotasEPerfis()
    {
        var dados = new TheoryData<string, string>();
        foreach (var rota in Casos.Keys)
            foreach (var perfil in new[] { nameof(Perfil.Admin), nameof(Perfil.Consulta) })
                dados.Add(rota, perfil);
        return dados;
    }

    [Fact]
    public void Toda_rota_tenant_scoped_tem_caso_cadastrado()
    {
        var fonte = _api.Services.GetRequiredService<EndpointDataSource>();
        var rotas = fonte.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RotaTenantScoped>() is not null)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => $"{m} {e.RoutePattern.RawText!.TrimEnd('/')}"))
            .Order().ToList();

        Assert.NotEmpty(rotas);
        Assert.Equal(Casos.Keys.Order(), rotas);
    }

    [Theory]
    [MemberData(nameof(RotasEPerfis))]
    public async Task Usuario_do_tenant_A_nao_le_nem_altera_dados_do_tenant_B(string rota, string perfil)
    {
        var a = await Dados.NovoTenantAsync(banco, "Cliente Isolamento A");
        var usuarioA = await Dados.NovoUsuarioAsync(banco, Enum.Parse<Perfil>(perfil), a);
        var token = Acesso.Token(usuarioA, a);
        var b = await NovoTenantBAsync();
        var antes = await DadosTenant.ImpressaoAsync(banco, b.Tenant.Id);
        var caso = Casos[rota];

        foreach (var tentativa in caso.Tentativas(b, token))
        {
            using var resposta = await _cliente.SendAsync(tentativa.Requisicao);

            var (status, codigo) = perfil == nameof(Perfil.Consulta) && caso.SomenteAdmin
                ? (HttpStatusCode.Forbidden, "ACESSO_NEGADO")
                : tentativa.ViaCabecalho
                    ? (HttpStatusCode.Forbidden, ValidacaoContextoTenantFilter.CodigoTenantDivergente)
                    : (HttpStatusCode.NotFound, TenantsEndpoints.CodigoNaoEncontrado);
            var descricao = $"{tentativa.Requisicao.Method} {tentativa.Requisicao.RequestUri} (X-Tenant: {tentativa.ViaCabecalho})";
            Assert.True(status == resposta.StatusCode, $"{descricao}: esperado {status}, veio {resposta.StatusCode}");
            Assert.Equal(codigo, await Sessao.CodigoAsync(resposta));
        }

        Assert.Equal(antes, await DadosTenant.ImpressaoAsync(banco, b.Tenant.Id));
    }

    [Fact]
    public async Task Toda_tabela_com_TENANT_ID_tem_indice_iniciado_por_TENANT_ID()
    {
        var tabelas = await DadosTenant.TabelasComTenantIdAsync(banco);
        var semIndice = new List<string>();
        foreach (var tabela in tabelas)
        {
            var indices = await banco.ConsultarEscalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID('dbo.{tabela}') AND c.name = 'TENANT_ID'
                """);
            if (indices == 0) semIndice.Add(tabela);
        }

        Assert.Contains("EMPRESA", tabelas);
        Assert.Contains("USUARIO", tabelas);
        Assert.Contains("REFRESH_TOKEN", tabelas);
        Assert.Empty(semIndice);
    }

    private async Task<TenantB> NovoTenantBAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Isolamento B");
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        // Segundo Admin: a inativação do primeiro não esbarra na regra do último Admin.
        await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        await using var contexto = banco.CriarContexto();
        var empresa = new Empresa { TenantId = tenant.Id, CodEmpresa = 1, Nome = "Matriz B" };
        contexto.Empresas.Add(empresa);
        await contexto.SaveChangesAsync();
        return new TenantB(tenant, empresa, usuario);
    }
}
