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
        // O worker de exclusão fica desligado; os testes chamam o ProcessadorExclusao diretamente.
        builder.UseSetting("Exclusao:WorkerAtivo", "false");
    }
}
