using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Seguranca;

public sealed record CorpoComTenant(Guid? TenantId) : IReferenciaTenant;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ValidacaoContextoTenantFilterTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/teste/tenant";

    private HostDeTeste _host = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _host = await HostDeTeste.IniciarAsync(s => Acesso.Servicos(s, banco), app =>
        {
            Acesso.Pipeline(app);
            var grupo = app.MapGroup(Rota).RequireAuthorization(Policies.LeituraTenant).ValidarContextoTenant();
            grupo.MapGet("", () => Results.Ok());
            grupo.MapPost("", (CorpoComTenant corpo) => Results.Ok());
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<(HttpStatusCode Status, string? Codigo)> EnviarAsync(HttpRequestMessage requisicao)
    {
        using var resposta = await _host.Cliente.SendAsync(requisicao);
        var texto = await resposta.Content.ReadAsStringAsync();
        if (resposta.IsSuccessStatusCode) return (resposta.StatusCode, null);
        using var json = JsonDocument.Parse(texto);
        return (resposta.StatusCode, json.RootElement.GetProperty("codigo").GetString());
    }

    private async Task<(Guid Usuario, string Token, Tenant Tenant)> AdminAsync()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var usuario = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        return (usuario.IdPublico, Acesso.Token(usuario, tenant), tenant);
    }

    private async Task<List<RegistroAuditoria>> AuditoriaDoUsuarioAsync(Guid usuario)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Auditoria.AsNoTracking().Where(r => r.UsuarioIdPublico == usuario).ToListAsync();
    }

    [Fact]
    public async Task Admin_com_X_Tenant_de_outro_tenant_recebe_403_e_gera_um_registro_de_auditoria()
    {
        var (usuario, token, tenant) = await AdminAsync();
        var outro = await Dados.NovoTenantAsync(banco);

        var resultado = await EnviarAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token, outro.IdPublico));

        Assert.Equal((HttpStatusCode.Forbidden, "TENANT_DIVERGENTE"), resultado);
        var registro = Assert.Single(await AuditoriaDoUsuarioAsync(usuario));
        Assert.Equal("ACESSO_CRUZADO", registro.Operacao);
        Assert.Equal("Negado", registro.Resultado);
        Assert.Equal("Admin", registro.Perfil);
        Assert.Equal(outro.IdPublico, registro.TenantIdPublico);
        Assert.Contains(tenant.IdPublico.ToString(), registro.Detalhes);
    }

    [Fact]
    public async Task Admin_com_X_Tenant_igual_ao_token_recebe_200()
    {
        var (usuario, token, tenant) = await AdminAsync();

        var resultado = await EnviarAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token, tenant.IdPublico));

        Assert.Equal(HttpStatusCode.OK, resultado.Status);
        Assert.Empty(await AuditoriaDoUsuarioAsync(usuario));
    }

    [Fact]
    public async Task Corpo_com_tenant_divergente_recebe_403_e_auditoria()
    {
        var (usuario, token, _) = await AdminAsync();
        var outro = await Dados.NovoTenantAsync(banco);
        var requisicao = Acesso.Requisicao(HttpMethod.Post, Rota, token);
        requisicao.Content = JsonContent.Create(new CorpoComTenant(outro.IdPublico));

        var resultado = await EnviarAsync(requisicao);

        Assert.Equal((HttpStatusCode.Forbidden, "TENANT_DIVERGENTE"), resultado);
        Assert.Equal(outro.IdPublico, Assert.Single(await AuditoriaDoUsuarioAsync(usuario)).TenantIdPublico);
    }

    [Fact]
    public async Task SuperAdmin_com_X_Tenant_recebe_200()
    {
        var superAdmin = await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin);
        var tenant = await Dados.NovoTenantAsync(banco);
        var requisicao = Acesso.Requisicao(HttpMethod.Post, Rota, Acesso.Token(superAdmin, null), tenant.IdPublico);
        requisicao.Content = JsonContent.Create(new CorpoComTenant(tenant.IdPublico));

        Assert.Equal(HttpStatusCode.OK, (await EnviarAsync(requisicao)).Status);
        Assert.Empty(await AuditoriaDoUsuarioAsync(superAdmin.IdPublico));
    }

    [Fact]
    public async Task X_Tenant_mal_formatado_recebe_400()
    {
        var (_, token, _) = await AdminAsync();

        var resultado = await EnviarAsync(Acesso.Requisicao(HttpMethod.Get, Rota, token, xTenantTexto: "nao-e-guid"));

        Assert.Equal((HttpStatusCode.BadRequest, "REQUISICAO_INVALIDA"), resultado);
    }
}
