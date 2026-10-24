using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class AuditoriaExclusaoEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static ExclusaoTenant NovoJob(int tenantId, SituacaoExclusao situacao) => new()
    {
        TenantId = tenantId,
        TenantIdPublico = Guid.NewGuid(),
        TenantNome = "Tenant em exclusão",
        Situacao = situacao,
        SolicitadoPorIdPublico = Guid.NewGuid(),
    };

    [Fact]
    public async Task Segundo_job_Pendente_para_o_mesmo_tenant_e_recusado()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        await using (var contexto = banco.CriarContexto())
        {
            contexto.ExclusoesTenant.Add(NovoJob(tenant, SituacaoExclusao.Pendente));
            await contexto.SaveChangesAsync();
        }

        await using var outro = banco.CriarContexto();
        outro.ExclusoesTenant.Add(NovoJob(tenant, SituacaoExclusao.Pendente));
        var erro = await Dados.FalhaSqlAsync(() => outro.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_EXCLUSAO_TENANT_TENANT_ID", erro.Message);
    }

    [Fact]
    public async Task Job_Concluida_e_novo_Pendente_para_o_mesmo_tenant_sao_aceitos()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        await using var contexto = banco.CriarContexto();
        contexto.ExclusoesTenant.Add(NovoJob(tenant, SituacaoExclusao.Concluida));
        contexto.ExclusoesTenant.Add(NovoJob(tenant, SituacaoExclusao.Pendente));

        await contexto.SaveChangesAsync();

        var situacoes = await contexto.ExclusoesTenant.AsNoTracking()
            .Where(j => j.TenantId == tenant).Select(j => j.Situacao).ToListAsync();
        Assert.Equal(2, situacoes.Count);
        Assert.Contains(SituacaoExclusao.Concluida, situacoes);
        Assert.Contains(SituacaoExclusao.Pendente, situacoes);
    }

    [Fact]
    public async Task Registro_de_auditoria_aceita_tenant_inexistente()
    {
        var tenantExcluido = Guid.NewGuid();
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Auditoria.Add(new RegistroAuditoria
            {
                OcorridoEm = DateTime.UtcNow,
                Operacao = "EXCLUSAO_TENANT_CONCLUIDA",
                TenantIdPublico = tenantExcluido,
                TenantNome = "Tenant já excluído",
                Resultado = "Sucesso",
            });
            await contexto.SaveChangesAsync();
        }

        await using var leitura = banco.CriarContexto();
        var registro = await leitura.Auditoria.AsNoTracking().SingleAsync(r => r.TenantIdPublico == tenantExcluido);
        Assert.Equal("Tenant já excluído", registro.TenantNome);
    }
}
