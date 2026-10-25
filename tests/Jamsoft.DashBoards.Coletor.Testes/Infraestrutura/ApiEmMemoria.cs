using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ColetorApi = Jamsoft.DashBoards.Api.Modulos.Coletores.Coletor;

namespace Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

/// <summary>
/// A API real em memória sobre o banco da coleção, com as mesmas definições do <c>ApiFactory</c> dos testes da API
/// (workers de exclusão e de limpeza de lotes desligados). Os <c>ApiFactory</c>/<c>Dados</c> da API não são
/// reaproveitados por link porque, neste assembly, <c>Coletor</c> resolve para o namespace
/// <c>Jamsoft.DashBoards.Coletor</c>.
/// </summary>
public sealed class ApiEmMemoria(BancoDeTestes banco) : WebApplicationFactory<Program>
{
    public const string ChaveJwt = "chave-jwt-usada-somente-nos-testes-do-coletor-0123456789";
    public const string UrlApi = "http://localhost/";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:Plataforma", banco.ConnectionString);
        builder.UseSetting("Jwt:Chave", ChaveJwt);
        builder.UseSetting("Exclusao:WorkerAtivo", "false");
        builder.UseSetting("LimpezaLotes:Ativa", "false");
    }
}

/// <summary>Dados da plataforma para os testes do Coletor: tenant, coletor autorizado e contagem de vendas.</summary>
public static class DadosPlataforma
{
    public static async Task<Tenant> NovoTenantAsync(BancoDeTestes banco, string nome = "Cliente do Coletor")
    {
        await using var contexto = banco.CriarContexto();
        // 14 dígitos aleatórios: a gravação direta não valida os dígitos verificadores.
        var tenant = new Tenant { Nome = nome, Cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10))) };
        contexto.Tenants.Add(tenant);
        await contexto.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Coletor ativo autorizado aos <paramref name="tenants"/> e a credencial que o Coletor usaria.</summary>
    public static async Task<(ColetorApi Coletor, CredencialApi Credencial)> NovoColetorAsync(BancoDeTestes banco, params Tenant[] tenants)
    {
        var segredo = SegredoColetor.Gerar();
        await using var contexto = banco.CriarContexto();
        var coletor = new ColetorApi { Nome = "Coletor de teste", ClientId = Guid.NewGuid(), SecretHash = SegredoColetor.Hash(segredo) };
        contexto.Coletores.Add(coletor);
        await contexto.SaveChangesAsync();
        contexto.ColetoresTenant.AddRange(tenants.Select(t => new ColetorTenant { TenantId = t.Id, ColetorId = coletor.Id }));
        await contexto.SaveChangesAsync();
        return (coletor, new CredencialApi(ApiEmMemoria.UrlApi, coletor.ClientId, segredo));
    }

    public static async Task<int> VendasAsync(BancoDeTestes banco, Tenant tenant)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
        return await contexto.Vendas.CountAsync();
    }
}
