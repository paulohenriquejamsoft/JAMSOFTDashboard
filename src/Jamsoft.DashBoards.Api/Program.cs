using Jamsoft.DashBoards.Api.Cli;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Seguranca;

var builder = WebApplication.CreateBuilder(args);

// Comandos de implantação (migrar, criar-superadmin) rodam e saem sem subir o servidor.
if (ComandosCli.TentarExecutar(args, builder.Configuration)) return;

builder.Services.AddErrosApi();
builder.Services.AddDadosPlataforma(builder.Configuration.GetConnectionString("Plataforma"));
builder.Services.AddAuditoria();
builder.Services.AddSegurancaApi();
builder.Services.AddAutenticacao();

var app = builder.Build();

app.UseErrosApi();
app.UseSegurancaApi();

// Sem autenticação: só para monitoramento (IIS, load balancer).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapAutenticacao();

app.Run();

// Exposto para os testes de integração (WebApplicationFactory<Program>).
public partial class Program;
