using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>
/// Host mínimo em memória para testar um componente de infraestrutura da API com rotas de teste,
/// usando os mesmos métodos de registro (AddXxx/UseXxx) que o Program.cs.
/// </summary>
public sealed class HostDeTeste : IAsyncDisposable
{
    private readonly WebApplication _app;

    private HostDeTeste(WebApplication app)
    {
        _app = app;
        Cliente = app.GetTestClient();
    }

    public HttpClient Cliente { get; }
    public IServiceProvider Servicos => _app.Services;

    public static async Task<HostDeTeste> IniciarAsync(
        Action<IServiceCollection> servicos,
        Action<WebApplication> pipeline)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testes" });
        builder.WebHost.UseTestServer();
        servicos(builder.Services);

        var app = builder.Build();
        pipeline(app);
        await app.StartAsync();
        return new HostDeTeste(app);
    }

    public async ValueTask DisposeAsync()
    {
        Cliente.Dispose();
        await _app.DisposeAsync();
    }
}
