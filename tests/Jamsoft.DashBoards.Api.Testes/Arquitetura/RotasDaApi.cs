using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jamsoft.DashBoards.Api.Testes.Arquitetura;

/// <summary>
/// Rotas mapeadas pelo Program.cs, lidas do <see cref="EndpointDataSource"/> de uma API em memória que nunca abre
/// conexão com o banco (o worker de exclusão fica desligado).
/// </summary>
public static class RotasDaApi
{
    private static readonly Lazy<IReadOnlyList<RouteEndpoint>> Lista = new(Carregar);

    public static IReadOnlyList<RouteEndpoint> Todas => Lista.Value;

    /// <summary>"MÉTODO /rota" para mensagens de falha.</summary>
    public static string Descrever(RouteEndpoint rota) =>
        $"{string.Join(",", rota.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()?.HttpMethods ?? ["*"])} " +
        rota.RoutePattern.RawText;

    private static IReadOnlyList<RouteEndpoint> Carregar()
    {
        using var api = new ApiSemBanco();
        return [.. api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];
    }

    private sealed class ApiSemBanco : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testes");
            builder.UseSetting("ConnectionStrings:Plataforma", "Server=nao-conecta;Database=nenhum");
            builder.UseSetting("Jwt:Chave", Acesso.ChaveJwt);
            builder.UseSetting("Exclusao:WorkerAtivo", "false");
        }
    }
}
