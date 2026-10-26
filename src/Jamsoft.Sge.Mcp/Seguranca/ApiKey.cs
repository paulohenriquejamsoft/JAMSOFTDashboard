using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jamsoft.Sge.Mcp.Seguranca;

/// <summary>
/// Formato da chave: sge_{prefixo}_{segredo}
///   prefixo: 8 caracteres, gravado em claro no banco para localizar o registro;
///   segredo: 40 caracteres aleatórios. Só o hash SHA-256 da chave inteira é gravado.
/// Como a chave é aleatória e longa, SHA-256 basta (não é senha escolhida por pessoa).
/// </summary>
public static partial class ApiKey
{
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int TamanhoPrefixo = 8;
    private const int TamanhoSegredo = 40;

    [GeneratedRegex(@"^sge_(?<prefixo>[A-Za-z0-9]{8})_[A-Za-z0-9]{40}$")]
    private static partial Regex Formato();

    public static string Gerar()
    {
        var prefixo = RandomNumberGenerator.GetString(Alfabeto, TamanhoPrefixo);
        var segredo = RandomNumberGenerator.GetString(Alfabeto, TamanhoSegredo);
        return $"sge_{prefixo}_{segredo}";
    }

    /// <summary>Valida o formato e devolve o prefixo; false se a chave nem tem o formato esperado.</summary>
    public static bool TentarLerPrefixo(string? chave, out string prefixo)
    {
        prefixo = "";
        if (string.IsNullOrWhiteSpace(chave)) return false;
        var m = Formato().Match(chave);
        if (!m.Success) return false;
        prefixo = m.Groups["prefixo"].Value;
        return true;
    }

    public static byte[] Hash(string chave) => SHA256.HashData(Encoding.UTF8.GetBytes(chave));
}
