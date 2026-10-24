using System.Text;

namespace Jamsoft.DashBoards.Api.Configuracao;

/// <summary>
/// Seção "Jwt". As chaves ficam fora do código (user-secrets ou variável de ambiente <c>Jwt__Chave</c>).
/// Para rotacionar: a chave atual vira <see cref="ChaveSecundaria"/> e a nova entra em <see cref="Chave"/>.
/// </summary>
public sealed class JwtOptions
{
    public const string Secao = "Jwt";
    public const int TamanhoMinimoChaveBytes = 32;

    public string Emissor { get; set; } = "Jamsoft.DashBoards";
    public string Audiencia { get; set; } = "Jamsoft.DashBoards";

    /// <summary>Assina os tokens novos e valida os recebidos. Mínimo de 32 bytes (UTF-8).</summary>
    public string Chave { get; set; } = "";

    /// <summary>Opcional: só valida (chave anterior durante a rotação). Mínimo de 32 bytes quando informada.</summary>
    public string? ChaveSecundaria { get; set; }

    public bool Valida() =>
        ChaveForte(Chave) && (string.IsNullOrEmpty(ChaveSecundaria) || ChaveForte(ChaveSecundaria));

    private static bool ChaveForte(string chave) => Encoding.UTF8.GetByteCount(chave) >= TamanhoMinimoChaveBytes;
}
