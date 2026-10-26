using Dapper;
using Jamsoft.Sge.Mcp.Cli;
using Jamsoft.Sge.Mcp.Configuracao;
using Jamsoft.Sge.Mcp.Dados;
using Jamsoft.Sge.Mcp.Seguranca;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Comandos administrativos (gerar-chave, gerar-chave-mestra) rodam e saem sem subir o servidor.
if (ComandosCli.TentarExecutar(args, builder.Configuration)) return;

// venda_id → VendaId, Nome_Empresa → NomeEmpresa etc.
DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services.AddOptions<McpOptions>()
    .Bind(builder.Configuration.GetSection(McpOptions.Secao))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ChaveCriptografia), "Mcp:ChaveCriptografia não configurada.")
    .ValidateOnStart();

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ProtetorConexao>();
builder.Services.AddSingleton<IRepositorioChaves, RepositorioChaves>();
builder.Services.AddSingleton<VendasRepositorio>();

builder.Services
    .AddAuthentication(ApiKeyDefaults.Esquema)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Esquema, _ => { });
builder.Services.AddAuthorization();

builder.Services
    .AddMcpServer()
    // Stateless: cada requisição HTTP é independente, sem sessão em memória.
    // Funciona atrás de balanceador e combina com a autenticação por requisição.
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Sem autenticação: só para monitoramento (IIS, load balancer).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Endpoint MCP (Streamable HTTP). Exige api-key válida.
app.MapMcp("/mcp").RequireAuthorization();

app.Run();
