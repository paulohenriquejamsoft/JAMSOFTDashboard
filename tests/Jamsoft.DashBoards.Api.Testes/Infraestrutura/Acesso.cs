using System.Net.Http.Headers;
using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Chave JWT de teste, registro da segurança no HostDeTeste e emissão de tokens.</summary>
public static class Acesso
{
    public const string ChaveJwt = "chave-jwt-usada-somente-nos-testes-0123456789";

    /// <summary>Mesmos registros do Program.cs, com a chave de teste e o banco da coleção.</summary>
    public static void Servicos(IServiceCollection servicos, BancoDeTestes banco, TimeProvider? tempo = null)
    {
        if (tempo is not null) servicos.AddSingleton(tempo);
        servicos.AddErrosApi();
        servicos.AddDadosPlataforma(banco.ConnectionString);
        servicos.AddAuditoria();
        servicos.AddSegurancaApi();
        servicos.Configure<JwtOptions>(o => o.Chave = ChaveJwt);
    }

    public static void Pipeline(WebApplication app)
    {
        app.UseErrosApi();
        app.UseSegurancaApi();
    }

    public static string Token(Usuario usuario, Tenant? tenant, TimeProvider? tempo = null) =>
        new TokenService(Options.Create(new JwtOptions { Chave = ChaveJwt }), tempo ?? TimeProvider.System)
            .Emitir(usuario, tenant).Token;

    /// <summary>Token de Coletor (como o de <c>POST /api/coletor/token</c>) para os <paramref name="tenants"/>.</summary>
    public static string TokenColetor(Coletor coletor, IEnumerable<Guid> tenants, TimeProvider? tempo = null) =>
        new TokenService(Options.Create(new JwtOptions { Chave = ChaveJwt }), tempo ?? TimeProvider.System)
            .EmitirColetor(coletor, tenants).Token;

    /// <summary>Grava um SuperAdmin ativo e devolve um token dele.</summary>
    public static async Task<string> TokenSuperAdminAsync(BancoDeTestes banco) =>
        Token(await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin), null);

    /// <summary>Requisição autenticada com corpo JSON.</summary>
    public static HttpRequestMessage ComCorpo<T>(HttpMethod metodo, string rota, string? token, T corpo, Guid? xTenant = null)
    {
        var requisicao = Requisicao(metodo, rota, token, xTenant);
        requisicao.Content = System.Net.Http.Json.JsonContent.Create(corpo);
        return requisicao;
    }

    public static HttpRequestMessage Requisicao(HttpMethod metodo, string rota, string? token, Guid? xTenant = null,
        string? xTenantTexto = null)
    {
        var requisicao = new HttpRequestMessage(metodo, rota);
        if (token is not null) requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var cabecalho = xTenantTexto ?? xTenant?.ToString();
        if (cabecalho is not null) requisicao.Headers.Add(TenantAtual.CabecalhoTenant, cabecalho);
        return requisicao;
    }
}
