using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Auditoria;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class AuditoriaTestes(BancoDeTestes banco) : IAsyncLifetime, IAsyncDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 13, 45, 30, 123, TimeSpan.Zero);
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();
    private ServiceProvider _servicos = null!;
    private AsyncServiceScope _escopo;

    public Task InitializeAsync()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        var servicos = new ServiceCollection();
        servicos.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });
        servicos.AddSingleton<TimeProvider>(new FakeTimeProvider(Agora));
        servicos.AddScoped<ITenantAtual>(_ => new TenantFixo(1, Perfil.Admin, _tenant, _usuario));
        servicos.AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, banco.ConnectionString));
        servicos.AddAuditoria();
        _servicos = servicos.BuildServiceProvider();
        _escopo = _servicos.CreateAsyncScope();
        return banco.MigrarAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        await _escopo.DisposeAsync();
        await _servicos.DisposeAsync();
    }

    private IAuditoria Auditoria => _escopo.ServiceProvider.GetRequiredService<IAuditoria>();
    private PlataformaDbContext BancoDaRequisicao => _escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

    private async Task<List<RegistroAuditoria>> GravadosAsync(Guid recurso)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Auditoria.AsNoTracking().Where(r => r.RecursoIdPublico == recurso).ToListAsync();
    }

    [Fact]
    public async Task Registrar_so_persiste_com_SaveChanges_e_rollback_descarta()
    {
        var descartado = Guid.NewGuid();
        await using (var transacao = await BancoDaRequisicao.Database.BeginTransactionAsync())
        {
            Auditoria.Registrar(OperacaoAuditada.AcessoCruzado, descartado, _tenant, "Tenant", ResultadoAuditoria.Sucesso);
            await BancoDaRequisicao.SaveChangesAsync();
            await transacao.RollbackAsync();
        }
        BancoDaRequisicao.ChangeTracker.Clear();

        var confirmado = Guid.NewGuid();
        Auditoria.Registrar(OperacaoAuditada.AcessoCruzado, confirmado, _tenant, "Tenant", ResultadoAuditoria.Sucesso);
        Assert.Empty(await GravadosAsync(confirmado));
        await BancoDaRequisicao.SaveChangesAsync();

        Assert.Empty(await GravadosAsync(descartado));
        Assert.Single(await GravadosAsync(confirmado));
    }

    [Fact]
    public async Task RegistrarAgoraAsync_persiste_mesmo_com_a_transacao_da_requisicao_desfeita()
    {
        var recurso = Guid.NewGuid();
        await using (var transacao = await BancoDaRequisicao.Database.BeginTransactionAsync())
        {
            await Auditoria.RegistrarAgoraAsync(OperacaoAuditada.AcessoCruzado, recurso, _tenant, "Tenant",
                ResultadoAuditoria.Negado);
            await transacao.RollbackAsync();
        }

        Assert.Single(await GravadosAsync(recurso));
    }

    [Fact]
    public async Task Detalhes_saem_sem_senha_token_e_secret()
    {
        var recurso = Guid.NewGuid();
        await Auditoria.RegistrarAgoraAsync(OperacaoAuditada.AcessoCruzado, recurso, _tenant, "Tenant",
            ResultadoAuditoria.Sucesso, new
            {
                Nome = "Fulano",
                Senha = "segredo-1",
                AdminInicial = new { Email = "a@b.com", SenhaInicial = "segredo-2" },
                RefreshToken = "segredo-3",
                ClientSecret = "segredo-4",
            });

        var detalhes = Assert.Single(await GravadosAsync(recurso)).Detalhes!;
        using var json = JsonDocument.Parse(detalhes);
        Assert.Equal("Fulano", json.RootElement.GetProperty("nome").GetString());
        Assert.Equal("a@b.com", json.RootElement.GetProperty("adminInicial").GetProperty("email").GetString());
        Assert.False(json.RootElement.TryGetProperty("senha", out _));
        Assert.False(json.RootElement.GetProperty("adminInicial").TryGetProperty("senhaInicial", out _));
        Assert.False(json.RootElement.TryGetProperty("refreshToken", out _));
        Assert.False(json.RootElement.TryGetProperty("clientSecret", out _));
        Assert.DoesNotContain("segredo", detalhes);
    }

    [Fact]
    public async Task Registro_preenche_todos_os_campos_do_AUD_02()
    {
        var recurso = Guid.NewGuid();
        await Auditoria.RegistrarAgoraAsync(OperacaoAuditada.AcessoCruzado, recurso, _tenant, "Tenant Auditado",
            ResultadoAuditoria.Negado);

        var registro = Assert.Single(await GravadosAsync(recurso));
        Assert.Equal(Agora.UtcDateTime, registro.OcorridoEm);
        Assert.Equal(_usuario, registro.UsuarioIdPublico);
        Assert.Equal("Admin", registro.Perfil);
        Assert.Equal("ACESSO_CRUZADO", registro.Operacao);
        Assert.Equal(recurso, registro.RecursoIdPublico);
        Assert.Equal(_tenant, registro.TenantIdPublico);
        Assert.Equal("Tenant Auditado", registro.TenantNome);
        Assert.Equal("203.0.113.7", registro.Ip);
        Assert.Equal("Negado", registro.Resultado);
    }
}
