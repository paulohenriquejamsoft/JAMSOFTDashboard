using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Seguranca;

/// <summary>Escrita de entidade de outro tenant numa requisição: 403 ACESSO_NEGADO + auditoria (ISO-02/ISO-04).</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class AcessoCruzadoTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/teste/gravar-em-outro-tenant/{tenantId:int}";

    private HostDeTeste _host = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _host = await HostDeTeste.IniciarAsync(s => Acesso.Servicos(s, banco), app =>
        {
            Acesso.Pipeline(app);
            app.MapPost(Rota, async (int tenantId, PlataformaDbContext contexto) =>
            {
                contexto.Empresas.Add(new Empresa { TenantId = tenantId, CodEmpresa = 1, Nome = "Intrusa" });
                await contexto.SaveChangesAsync();
                return Results.Ok();
            }).RequireAuthorization(Policies.LeituraTenant);
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Gravar_entidade_de_outro_tenant_responde_403_ACESSO_NEGADO_e_registra_auditoria()
    {
        var tenantA = await Dados.NovoTenantAsync(banco, "Tenant A");
        var tenantB = await Dados.NovoTenantAsync(banco, "Tenant B");
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenantA);

        using var resposta = await _host.Cliente.SendAsync(Acesso.Requisicao(HttpMethod.Post,
            $"/teste/gravar-em-outro-tenant/{tenantB.Id}", Acesso.Token(admin, tenantA)));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal("ACESSO_NEGADO", json.RootElement.GetProperty("codigo").GetString());

        await using var contexto = banco.CriarContexto();
        var registro = Assert.Single(await contexto.Auditoria.AsNoTracking()
            .Where(r => r.UsuarioIdPublico == admin.IdPublico).ToListAsync());
        Assert.Equal("ACESSO_CRUZADO", registro.Operacao);
        Assert.Equal("Negado", registro.Resultado);
        Assert.Equal(tenantB.IdPublico, registro.TenantIdPublico);
        Assert.Equal("Tenant B", registro.TenantNome);
        Assert.Empty(await contexto.Empresas.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
            .Where(e => e.TenantId == tenantB.Id).ToListAsync());
    }
}
