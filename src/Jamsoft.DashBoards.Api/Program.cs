var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Sem autenticação: só para monitoramento (IIS, load balancer).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

// Exposto para os testes de integração (WebApplicationFactory<Program>).
public partial class Program;
