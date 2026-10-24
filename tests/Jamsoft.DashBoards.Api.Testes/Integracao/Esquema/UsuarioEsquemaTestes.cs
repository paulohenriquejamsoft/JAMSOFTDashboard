using System.Security.Cryptography;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Esquema;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class UsuarioEsquemaTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Usuario NovoUsuario(Perfil perfil, int? tenantId, string? email = null) => new()
    {
        Nome = "Usuário de teste",
        Email = email ?? $"{Guid.NewGuid():N}@teste.local",
        SenhaHash = "hash",
        Perfil = perfil,
        TenantId = tenantId,
    };

    [Fact]
    public async Task Email_duplicado_e_recusado_mesmo_em_outro_tenant()
    {
        var tenantA = await Dados.CriarTenantAsync(banco);
        var tenantB = await Dados.CriarTenantAsync(banco);
        var email = $"{Guid.NewGuid():N}@teste.local";
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Usuarios.Add(NovoUsuario(Perfil.Admin, tenantA, email));
            await contexto.SaveChangesAsync();
        }

        await using var outro = banco.CriarContexto();
        outro.Usuarios.Add(NovoUsuario(Perfil.Consulta, tenantB, email));
        var erro = await Dados.FalhaSqlAsync(() => outro.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_USUARIO_EMAIL", erro.Message);
    }

    [Fact]
    public async Task SuperAdmin_com_tenant_e_recusado()
    {
        var tenant = await Dados.CriarTenantAsync(banco);
        await using var contexto = banco.CriarContexto();
        contexto.Usuarios.Add(NovoUsuario(Perfil.SuperAdmin, tenant));

        var erro = await Dados.FalhaSqlAsync(() => contexto.SaveChangesAsync());

        Assert.Equal(Dados.ErroCheck, erro.Number);
        Assert.Contains("CK_USUARIO_PERFIL_TENANT", erro.Message);
    }

    [Theory]
    [InlineData(Perfil.Admin)]
    [InlineData(Perfil.Consulta)]
    public async Task Usuario_de_tenant_sem_tenant_e_recusado(Perfil perfil)
    {
        await using var contexto = banco.CriarContexto();
        contexto.Usuarios.Add(NovoUsuario(perfil, tenantId: null));

        var erro = await Dados.FalhaSqlAsync(() => contexto.SaveChangesAsync());

        Assert.Equal(Dados.ErroCheck, erro.Number);
        Assert.Contains("CK_USUARIO_PERFIL_TENANT", erro.Message);
    }

    [Fact]
    public async Task Hash_de_refresh_token_duplicado_e_recusado()
    {
        var superAdmin = NovoUsuario(Perfil.SuperAdmin, tenantId: null);
        var hash = SHA256.HashData(Guid.NewGuid().ToByteArray());
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Usuarios.Add(superAdmin);
            await contexto.SaveChangesAsync();
            contexto.RefreshTokens.Add(NovoToken(superAdmin.Id, hash));
            await contexto.SaveChangesAsync();
        }

        await using var outro = banco.CriarContexto();
        outro.RefreshTokens.Add(NovoToken(superAdmin.Id, hash));
        var erro = await Dados.FalhaSqlAsync(() => outro.SaveChangesAsync());

        Assert.Equal(Dados.ErroIndiceUnico, erro.Number);
        Assert.Contains("IX_REFRESH_TOKEN_HASH", erro.Message);
    }

    private static RefreshToken NovoToken(int usuarioId, byte[] hash) => new()
    {
        UsuarioId = usuarioId,
        Hash = hash,
        Familia = Guid.NewGuid(),
        ExpiraEm = DateTime.UtcNow.AddDays(7),
    };
}
