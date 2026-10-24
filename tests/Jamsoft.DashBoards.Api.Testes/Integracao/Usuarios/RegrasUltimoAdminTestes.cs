using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.Data.SqlClient;
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

    [Fact]
    public async Task Dois_Admins_se_rebaixando_ao_mesmo_tempo_exatamente_um_falha()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admins = new[]
        {
            await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant),
            await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant),
        };
        var chamaram = 0;
        var ambasChamaram = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> RebaixarAsync(int alvoId)
        {
            await using var contexto = banco.CriarContexto(new TenantFixo(tenant.Id));
            await using var transacao = await contexto.Database.BeginTransactionAsync();
            try
            {
                var garantia = new RegrasUltimoAdmin(contexto).GarantirAsync(tenant.Id, alvoId);
                if (Interlocked.Increment(ref chamaram) == 2) ambasChamaram.SetResult();
                await garantia;
                // Ninguém confirma antes de as duas transações terem chamado GarantirAsync; a pausa garante que a
                // consulta da outra já chegou ao SQL Server (sem o lock, as duas veriam 1 Admin e passariam).
                await ambasChamaram.Task;
                await Task.Delay(500);

                var usuario = await contexto.Usuarios.SingleAsync(u => u.Id == alvoId);
                usuario.Perfil = Perfil.Consulta;
                await contexto.SaveChangesAsync();
                await transacao.CommitAsync();
                return "ok";
            }
            catch (ProblemaApiException erro) when (erro.Codigo == "ULTIMO_ADMIN")
            {
                return "ULTIMO_ADMIN";
            }
            catch (Exception erro) when (erro is SqlException { Number: 1205 } || erro.InnerException is SqlException { Number: 1205 })
            {
                return "DEADLOCK";
            }
        }

        var resultados = await Task.WhenAll(admins.Select(a => Task.Run(() => RebaixarAsync(a.Id))));

        // Caso esperado: a segunda transação espera o lock da primeira e falha com ULTIMO_ADMIN. Um deadlock
        // (o SQL Server desfaz uma delas) também deixa exatamente uma falha; o resultado fica no log do teste.
        saida.WriteLine($"Resultados: {string.Join(", ", resultados)}");
        Assert.Equal(1, resultados.Count(r => r == "ok"));
        Assert.Equal(1, resultados.Count(r => r is "ULTIMO_ADMIN" or "DEADLOCK"));
        Assert.Equal(1, await banco.ConsultarEscalarAsync<int>(
            $"SELECT COUNT(*) FROM USUARIO WHERE TENANT_ID = {tenant.Id} AND PERFIL = 'Admin' AND ATIVO = 1"));
    }
}
