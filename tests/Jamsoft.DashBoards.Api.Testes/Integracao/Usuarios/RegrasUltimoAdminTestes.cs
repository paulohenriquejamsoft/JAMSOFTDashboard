using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Usuarios;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class RegrasUltimoAdminTestes(BancoDeTestes banco, ITestOutputHelper saida) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task GarantirAsync(int tenantId, int alvoId)
    {
        await using var contexto = banco.CriarContexto(new TenantFixo(tenantId));
        await using var transacao = await contexto.Database.BeginTransactionAsync();
        await new RegrasUltimoAdmin(contexto).GarantirAsync(tenantId, alvoId);
    }

    [Fact]
    public async Task Unico_Admin_ativo_lanca_422_ULTIMO_ADMIN()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        // Não contam: Admin inativo, usuário Consulta e Admin de outro tenant.
        await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant, ativo: false);
        await Dados.NovoUsuarioAsync(banco, Perfil.Consulta, tenant);
        await Dados.NovoUsuarioAsync(banco, Perfil.Admin, await Dados.NovoTenantAsync(banco));

        var erro = await Assert.ThrowsAsync<ProblemaApiException>(() => GarantirAsync(tenant.Id, admin.Id));

        Assert.Equal(422, erro.Status);
        Assert.Equal("ULTIMO_ADMIN", erro.Codigo);
        Assert.Equal("O tenant precisa de ao menos um Admin ativo", erro.Titulo);
    }

    [Fact]
    public async Task Com_dois_Admins_ativos_permite_alterar_um_deles()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);

        var erro = await Record.ExceptionAsync(() => GarantirAsync(tenant.Id, admin.Id));

        Assert.Null(erro);
    }

    /// <summary>
    /// Sincronização determinística, sem pausa: A passa pela regra e segura o lock; B chama a regra e o teste só
    /// segue quando vê, em <c>sys.dm_exec_requests</c>, a sessão de B bloqueada pela de A. Aí A rebaixa e confirma,
    /// e B tem de enxergar isso (422). Sem <c>UPDLOCK, HOLDLOCK</c> B não espera: a regra de B termina antes de A
    /// confirmar e o teste falha.
    /// </summary>
    [Fact]
    public async Task Dois_Admins_se_rebaixando_ao_mesmo_tempo_exatamente_um_falha()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var adminA = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);
        var adminB = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);

        await using var contextoA = banco.CriarContexto(new TenantFixo(tenant.Id));
        await using var transacaoA = await contextoA.Database.BeginTransactionAsync();
        await using var contextoB = banco.CriarContexto(new TenantFixo(tenant.Id));
        await using var transacaoB = await contextoB.Database.BeginTransactionAsync();
        var sessaoA = await SessaoAsync(contextoA);
        var sessaoB = await SessaoAsync(contextoB);

        await new RegrasUltimoAdmin(contextoA).GarantirAsync(tenant.Id, adminA.Id);
        var garantiaB = Task.Run(() => new RegrasUltimoAdmin(contextoB).GarantirAsync(tenant.Id, adminB.Id));

        var limite = DateTime.UtcNow.AddSeconds(60);
        var bloqueada = false;
        while (!bloqueada && !garantiaB.IsCompleted && DateTime.UtcNow < limite)
        {
            bloqueada = await banco.ConsultarEscalarAsync<int>(
                $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = {sessaoB} AND blocking_session_id = {sessaoA}") > 0;
            if (!bloqueada) await Task.Delay(20);
        }
        saida.WriteLine($"Sessão B bloqueada pela A: {bloqueada}; regra de B já terminou: {garantiaB.IsCompleted}");
        Assert.False(garantiaB.IsCompleted, "A regra de B terminou enquanto a transação de A ainda estava aberta.");
        Assert.True(bloqueada, "A sessão de B não ficou esperando o lock da sessão de A.");

        var usuarioA = await contextoA.Usuarios.SingleAsync(u => u.Id == adminA.Id);
        usuarioA.Perfil = Perfil.Consulta;
        await contextoA.SaveChangesAsync();
        await transacaoA.CommitAsync();

        var erro = await Assert.ThrowsAsync<ProblemaApiException>(() => garantiaB);
        Assert.Equal(422, erro.Status);
        Assert.Equal("ULTIMO_ADMIN", erro.Codigo);
        await transacaoB.RollbackAsync();
        Assert.Equal(1, await banco.ConsultarEscalarAsync<int>(
            $"SELECT COUNT(*) FROM USUARIO WHERE TENANT_ID = {tenant.Id} AND PERFIL = 'Admin' AND ATIVO = 1"));
    }

    private static Task<short> SessaoAsync(PlataformaDbContext contexto) =>
        contexto.Database.SqlQuery<short>($"SELECT @@SPID AS [Value]").SingleAsync();
}
