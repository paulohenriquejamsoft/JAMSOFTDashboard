using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Sobe a API em memória apontando para o banco descartável da coleção.</summary>
public sealed class ApiFactory(BancoDeTestes banco) : WebApplicationFactory<Program>
{
    public BancoDeTestes Banco { get; } = banco;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:Plataforma", Banco.ConnectionString);
        builder.UseSetting("Jwt:Chave", Acesso.ChaveJwt);
        // Os workers de exclusão e de limpeza de lotes ficam desligados; os testes chamam a lógica diretamente.
        builder.UseSetting("Exclusao:WorkerAtivo", "false");
        builder.UseSetting("LimpezaLotes:Ativa", "false");
    }
}
