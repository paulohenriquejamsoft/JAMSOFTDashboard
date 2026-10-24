using System.Text;
using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Jamsoft.DashBoards.Api.Seguranca;

public sealed record TokenEmitido(string Token, DateTimeOffset ExpiraEm);

/// <summary>
/// Emite o access token (AUTH-01): JWT HS256 de 15 minutos com <c>sub</c>, <c>perfil</c>,
/// <c>tenant</c> (só para usuários de tenant), <c>trocar_senha</c> e <c>jti</c>.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> opcoes, TimeProvider tempo)
{
    public static readonly TimeSpan Duracao = TimeSpan.FromMinutes(15);

    private static readonly JsonWebTokenHandler Manipulador = new();

    public TokenEmitido Emitir(Usuario usuario, Tenant? tenant)
    {
        var jwt = opcoes.Value;
        var agora = tempo.GetUtcNow();
        var expiraEm = agora.Add(Duracao);

        var claims = new Dictionary<string, object>
        {
            [TiposClaim.Usuario] = usuario.IdPublico.ToString(),
            [TiposClaim.Perfil] = usuario.Perfil.ToString(),
            [TiposClaim.TrocarSenha] = usuario.TrocarSenha,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
        };
        if (usuario.Perfil != Perfil.SuperAdmin && tenant is not null)
            claims[TiposClaim.Tenant] = tenant.IdPublico.ToString();

        var token = Manipulador.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Emissor,
            Audience = jwt.Audiencia,
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expiraEm.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(Chave(jwt.Chave), SecurityAlgorithms.HmacSha256),
        });
        return new TokenEmitido(token, expiraEm);
    }

    /// <summary>Aceita tokens assinados com a chave principal ou com a secundária (rotação).</summary>
    public static TokenValidationParameters ParametrosValidacao(JwtOptions jwt)
    {
        var chaves = new List<SecurityKey> { Chave(jwt.Chave) };
        if (!string.IsNullOrEmpty(jwt.ChaveSecundaria)) chaves.Add(Chave(jwt.ChaveSecundaria));

        return new TokenValidationParameters
        {
            ValidIssuer = jwt.Emissor,
            ValidAudience = jwt.Audiencia,
            IssuerSigningKeys = chaves,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            // Emissor e validador são a mesma API: sem tolerância, o token vale no máximo 15 minutos.
            ClockSkew = TimeSpan.Zero,
            NameClaimType = TiposClaim.Usuario,
            RoleClaimType = TiposClaim.Perfil,
        };
    }

    private static SymmetricSecurityKey Chave(string chave) => new(Encoding.UTF8.GetBytes(chave));
}

public static class TokenServiceExtensoes
{
    public static IServiceCollection AddTokenService(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.Secao)
            .Validate(o => o.Valida(),
                $"Jwt:Chave (e Jwt:ChaveSecundaria, se informada) precisa ter ao menos {JwtOptions.TamanhoMinimoChaveBytes} bytes.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TokenService>();
        return services;
    }
}
