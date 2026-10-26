namespace Jamsoft.DashBoards.Api.Configuracao;

/// <summary>Seção "Acesso": bloqueio por tentativas (AUTH-03) e limite de requisições no login.</summary>
public sealed class AcessoOptions
{
    public const string Secao = "Acesso";

    /// <summary>Falhas consecutivas que bloqueiam o login.</summary>
    public int TentativasAteBloqueio { get; set; } = 5;

    public int MinutosBloqueio { get; set; } = 15;

    /// <summary>Requisições a <c>/api/auth/login</c> por IP a cada minuto.</summary>
    public int LimiteLoginPorMinuto { get; set; } = 20;

    /// <summary>
    /// Frontend em outro site (outro domínio registrável) que a API: o cookie do refresh token vai com
    /// <c>SameSite=None; Partitioned</c>, porque com <c>Strict</c> o navegador não o envia nas chamadas do frontend.
    /// O CSRF do refresh continua barrado pelo cabeçalho <c>X-Requested-With</c> e pela lista de origens do CORS.
    /// </summary>
    public bool CookieEntreSites { get; set; }

    public bool Valida() => TentativasAteBloqueio > 0 && MinutosBloqueio > 0 && LimiteLoginPorMinuto > 0;
}
