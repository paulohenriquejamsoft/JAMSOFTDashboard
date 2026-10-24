using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddErrosApi();
builder.Services.AddDbContext<PlataformaDbContext>(o =>
    PlataformaDbContext.Configurar(o, builder.Configuration.GetConnectionString("Plataforma")));

var app = builder.Build();

app.UseErrosApi();

// Sem autenticação: só para monitoramento (IIS, load balancer).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

// Exposto para os testes de integração (WebApplicationFactory<Program>).
public partial class Program;
