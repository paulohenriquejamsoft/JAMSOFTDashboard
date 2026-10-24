using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Seguranca;

[Trait("Categoria", "Unidade")]
public sealed class TokenServiceTestes
{
    private const string ChaveA = "chave-de-teste-A-com-mais-de-32-bytes!!";
    private const string ChaveB = "chave-de-teste-B-com-mais-de-32-bytes!!";

    private static readonly Tenant TenantA = new() { IdPublico = Guid.NewGuid(), Nome = "A" };

    private static TokenService Servico(string chave, TimeProvider? tempo = null) =>
        new(Options.Create(new JwtOptions { Chave = chave }), tempo ?? TimeProvider.System);

    private static JsonWebToken Ler(string token) => new JsonWebTokenHandler().ReadJsonWebToken(token);

    [Fact]
    public void Token_de_Admin_tem_sub_perfil_tenant_trocar_senha_e_jti()
    {
        var usuario = new Usuario { IdPublico = Guid.NewGuid(), Perfil = Perfil.Admin, TrocarSenha = true };

        var jwt = Ler(Servico(ChaveA).Emitir(usuario, TenantA).Token);

        Assert.Equal(usuario.IdPublico.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal("Admin", jwt.GetClaim("perfil").Value);
        Assert.Equal(TenantA.IdPublico.ToString(), jwt.GetClaim("tenant").Value);
        Assert.True(jwt.GetPayloadValue<bool>("trocar_senha"));
        Assert.True(Guid.TryParse(jwt.GetClaim("jti").Value, out _));
        Assert.Equal("HS256", jwt.Alg);
    }

    [Fact]
    public void Token_de_SuperAdmin_nao_tem_tenant()
    {
        var usuario = new Usuario { IdPublico = Guid.NewGuid(), Perfil = Perfil.SuperAdmin, TrocarSenha = false };

        var jwt = Ler(Servico(ChaveA).Emitir(usuario, tenant: null).Token);

        Assert.Equal("SuperAdmin", jwt.GetClaim("perfil").Value);
        Assert.False(jwt.TryGetClaim("tenant", out _));
        Assert.False(jwt.GetPayloadValue<bool>("trocar_senha"));
    }

    [Fact]
    public void Token_expira_em_15_minutos()
    {
        var agora = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var usuario = new Usuario { IdPublico = Guid.NewGuid(), Perfil = Perfil.Consulta };

        var emitido = Servico(ChaveA, new FakeTimeProvider(agora)).Emitir(usuario, TenantA);
        var jwt = Ler(emitido.Token);

        Assert.Equal(agora.AddMinutes(15), emitido.ExpiraEm);
        Assert.Equal(agora.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(agora.UtcDateTime, jwt.IssuedAt);
    }

    [Fact]
    public void Chave_com_menos_de_32_bytes_falha_na_validacao_das_options()
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Chave"] = "curta-demais" })
            .Build();
        var servicos = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuracao)
            .AddTokenService()
            .BuildServiceProvider();

        var erro = Assert.Throws<OptionsValidationException>(() => servicos.GetRequiredService<IOptions<JwtOptions>>().Value);
        Assert.Contains("32 bytes", erro.Message);
    }

    [Fact]
    public async Task Token_assinado_com_a_chave_secundaria_e_aceito_e_chave_desconhecida_nao()
    {
        var usuario = new Usuario { IdPublico = Guid.NewGuid(), Perfil = Perfil.Admin };
        var tokenChaveAnterior = Servico(ChaveB).Emitir(usuario, TenantA).Token;
        var tokenChaveDesconhecida = Servico("chave-de-teste-C-com-mais-de-32-bytes!!").Emitir(usuario, TenantA).Token;
        var parametros = TokenService.ParametrosValidacao(new JwtOptions { Chave = ChaveA, ChaveSecundaria = ChaveB });
        var manipulador = new JsonWebTokenHandler();

        var aceito = await manipulador.ValidateTokenAsync(tokenChaveAnterior, parametros);
        var recusado = await manipulador.ValidateTokenAsync(tokenChaveDesconhecida, parametros);

        Assert.True(aceito.IsValid, aceito.Exception?.Message);
        Assert.False(recusado.IsValid);
    }
}
