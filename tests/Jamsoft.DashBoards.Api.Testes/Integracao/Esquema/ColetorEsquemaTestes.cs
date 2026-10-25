using System.Security.Cryptography;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

/// <summary>COLETOR e COLETOR_TENANT (COL-02, design "Data Models").</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ColetorEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Coletor NovoColetor(Guid? clientId = null, byte[]? secretHash = null) => new()
    {
        Nome = "Coletor de teste",
        ClientId = clientId ?? Guid.NewGuid(),
        SecretHash = secretHash ?? SHA256.HashData(Guid.NewGuid().ToByteArray()),
    };

    [Fact]
    public async Task CLIENT_ID_duplicado_e_recusado()
    {
        var clientId = Guid.NewGuid();
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Coletores.Add(NovoColetor(clientId));
            await contexto.SaveChangesAsync();
        }

        await using var duplicado = banco.CriarContexto();
        duplicado.Coletores.Add(NovoColetor(clientId));
        var erro = await Dados.FalhaSqlAsync(() => duplicado.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_COLETOR_CLIENT_ID", erro.Message);
    }

    [Fact]
    public async Task Mesmo_coletor_no_mesmo_tenant_duplicado_e_recusado()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        var coletor = await Dados.NovoColetorAsync(banco);
        await using (var contexto = banco.CriarContexto())
        {
            contexto.ColetoresTenant.Add(new ColetorTenant { TenantId = tenant, ColetorId = coletor.Id });
            await contexto.SaveChangesAsync();
        }

        await using var duplicado = banco.CriarContexto();
        duplicado.ColetoresTenant.Add(new ColetorTenant { TenantId = tenant, ColetorId = coletor.Id });
        var erro = await Dados.FalhaSqlAsync(() => duplicado.SaveChangesAsync());

        Assert.Equal(Dados.ErroChavePrimaria, erro.Number);
        Assert.Contains("PK_COLETOR_TENANT", erro.Message);
    }

    [Fact]
    public async Task SECRET_HASH_aceita_so_32_bytes()
    {
        await using (var contexto = banco.CriarContexto())
        {
            var aceito = NovoColetor(secretHash: RandomNumberGenerator.GetBytes(32));
            contexto.Coletores.Add(aceito);
            await contexto.SaveChangesAsync();
            Assert.Equal(32, await banco.ConsultarEscalarAsync<int>(
                $"SELECT DATALENGTH(SECRET_HASH) FROM COLETOR WHERE ID = {aceito.Id}"));
        }

        await using (var curto = banco.CriarContexto())
        {
            curto.Coletores.Add(NovoColetor(secretHash: RandomNumberGenerator.GetBytes(31)));
            var erro = await Dados.FalhaSqlAsync(() => curto.SaveChangesAsync());
            Assert.Equal(Dados.ErroCheck, erro.Number);
            Assert.Contains("CK_COLETOR_SECRET_HASH", erro.Message);
        }

        await using var longo = banco.CriarContexto();
        longo.Coletores.Add(NovoColetor(secretHash: RandomNumberGenerator.GetBytes(33)));
        var truncado = await Dados.FalhaSqlAsync(() => longo.SaveChangesAsync());
        Assert.Equal(Dados.ErroTruncamento, truncado.Number);
    }
}
