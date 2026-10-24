using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Auditoria;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class AuditoriaEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/admin/auditoria";

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

    /// <summary>Instante num passado distante e aleatório: períodos de testes diferentes não se cruzam.</summary>
    private static DateTime InstanteUnico() =>
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(0, 5_000_000));

    private static string OperacaoUnica() => $"TESTE_{Guid.NewGuid():N}"[..30].ToUpperInvariant();

    private static RegistroAuditoria Registro(DateTime ocorridoEm, string operacao = "TESTE_CONSULTA", Guid? tenant = null,
        Guid? usuario = null, string? email = null) => new()
        {
            OcorridoEm = ocorridoEm,
            Operacao = operacao,
            TenantIdPublico = tenant,
            UsuarioIdPublico = usuario,
            UsuarioEmail = email,
            Resultado = ResultadoAuditoria.Sucesso,
        };

    private async Task SemearAsync(params RegistroAuditoria[] registros)
    {
        await using var contexto = banco.CriarContexto();
        contexto.Auditoria.AddRange(registros);
        await contexto.SaveChangesAsync();
    }

    private async Task<JsonElement> ConsultarAsync(string consulta)
    {
        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, $"{Rota}?{consulta}", _token));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return await Sessao.JsonAsync(resposta);
    }

    private static List<long> Ids(JsonElement pagina) =>
        [.. pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("id").GetInt64())];

    private static string Iso(DateTime instante) => Uri.EscapeDataString(instante.ToString("O"));

    [Fact]
    public async Task Filtro_por_periodo_inclui_os_limites_e_ordena_do_mais_recente()
    {
        var base_ = InstanteUnico();
        RegistroAuditoria antes = Registro(base_), inicio = Registro(base_.AddHours(1)),
            fim = Registro(base_.AddHours(2)), depois = Registro(base_.AddHours(3));
        await SemearAsync(antes, inicio, fim, depois);

        var pagina = await ConsultarAsync($"de={Iso(base_.AddHours(1))}&ate={Iso(base_.AddHours(2))}");

        Assert.Equal([fim.Id, inicio.Id], Ids(pagina));
        Assert.Equal(base_.AddHours(2), pagina.GetProperty("itens")[0].GetProperty("ocorridoEm").GetDateTime());
    }

    [Fact]
    public async Task Filtro_por_tenant()
    {
        var tenant = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        RegistroAuditoria a = Registro(agora, tenant: tenant), b = Registro(agora.AddSeconds(1), tenant: tenant),
            outro = Registro(agora, tenant: Guid.NewGuid());
        await SemearAsync(a, b, outro);

        var pagina = await ConsultarAsync($"tenant={tenant}");

        Assert.Equal([b.Id, a.Id], Ids(pagina));
        Assert.Equal(2, pagina.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Filtro_por_usuario_aceita_ID_publico_ou_email()
    {
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin);
        var agora = DateTime.UtcNow;
        // Com usuário autenticado só o ID público é gravado; no login, também o e-mail.
        RegistroAuditoria porId = Registro(agora, usuario: usuario.IdPublico),
            comEmail = Registro(agora.AddSeconds(1), usuario: usuario.IdPublico, email: usuario.Email),
            outro = Registro(agora, usuario: Guid.NewGuid());
        await SemearAsync(porId, comEmail, outro);

        var peloId = await ConsultarAsync($"usuario={usuario.IdPublico}");
        var peloEmail = await ConsultarAsync($"usuario={Uri.EscapeDataString(usuario.Email.ToUpperInvariant())}");

        Assert.Equal([comEmail.Id, porId.Id], Ids(peloId));
        Assert.Equal([comEmail.Id, porId.Id], Ids(peloEmail));
    }

    [Fact]
    public async Task Filtro_por_operacao()
    {
        var operacao = OperacaoUnica();
        var agora = DateTime.UtcNow;
        RegistroAuditoria alvo = Registro(agora, operacao), outro = Registro(agora, OperacaoUnica());
        await SemearAsync(alvo, outro);

        var pagina = await ConsultarAsync($"operacao={operacao}");

        Assert.Equal([alvo.Id], Ids(pagina));
        Assert.Equal(operacao, pagina.GetProperty("itens")[0].GetProperty("operacao").GetString());
    }

    [Fact]
    public async Task Filtros_combinados_ordenados_por_OCORRIDO_EM_desc_e_paginados()
    {
        var base_ = InstanteUnico();
        var tenant = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var operacao = OperacaoUnica();
        RegistroAuditoria r1 = Registro(base_.AddMinutes(10), operacao, tenant, usuario),
            r2 = Registro(base_.AddMinutes(30), operacao, tenant, usuario),
            r3 = Registro(base_.AddMinutes(20), operacao, tenant, usuario),
            foraDoPeriodo = Registro(base_.AddDays(2), operacao, tenant, usuario),
            outroTenant = Registro(base_.AddMinutes(15), operacao, Guid.NewGuid(), usuario),
            outroUsuario = Registro(base_.AddMinutes(15), operacao, tenant, Guid.NewGuid()),
            outraOperacao = Registro(base_.AddMinutes(15), OperacaoUnica(), tenant, usuario);
        await SemearAsync(r1, r2, r3, foraDoPeriodo, outroTenant, outroUsuario, outraOperacao);
        var filtros = $"de={Iso(base_)}&ate={Iso(base_.AddDays(1))}&tenant={tenant}&usuario={usuario}&operacao={operacao}";

        var tudo = await ConsultarAsync(filtros);
        var segunda = await ConsultarAsync($"{filtros}&pagina=2&tamanho=2");

        Assert.Equal([r2.Id, r3.Id, r1.Id], Ids(tudo));
        Assert.Equal(3, tudo.GetProperty("total").GetInt32());
        Assert.Equal([r1.Id], Ids(segunda));
        Assert.Equal(2, segunda.GetProperty("pagina").GetInt32());
        Assert.Equal(2, segunda.GetProperty("tamanho").GetInt32());
    }

    [Fact]
    public async Task Admin_consultando_responde_403()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, Acesso.Token(admin, tenant)));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
    }

    [Fact]
    public async Task Nao_ha_rota_para_alterar_ou_excluir_registros()
    {
        var registro = Registro(DateTime.UtcNow, OperacaoUnica());
        await SemearAsync(registro);
        var rota = $"{Rota}/{registro.Id}";

        using var alterar = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, rota, _token, new { resultado = "Falha" }));
        using var excluir = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Delete, rota, _token));
        using var excluirColecao = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Delete, Rota, _token));

        Assert.Contains(alterar.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Contains(excluir.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Contains(excluirColecao.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        var pagina = await ConsultarAsync($"operacao={registro.Operacao}");
        Assert.Equal([registro.Id], Ids(pagina));
        Assert.Equal("Sucesso", pagina.GetProperty("itens")[0].GetProperty("resultado").GetString());
    }
}
