using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jamsoft.DashBoards.Contratos.Mcp;

/// <summary>
/// Api-key do servidor MCP, emitida pela API (<c>gerar-chave-mcp</c>) e validada pelo Jamsoft.Sge.Mcp.
/// Formato <c>sge_{prefixo}_{segredo}</c>: o prefixo (8 caracteres) fica em claro no banco para localizar o registro;
/// do resto só o SHA-256 da chave inteira é gravado. Aleatória e longa, não precisa de hash lento.
/// </summary>
public static partial class FormatoChaveMcp
{
    public const int TamanhoPrefixo = 8;
    public const int TamanhoSegredo = 40;
    public const int TamanhoHash = 32;

    /// <summary>Sem caracteres ambíguos (0/O, 1/l/I).</summary>
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    [GeneratedRegex(@"^sge_(?<prefixo>[A-Za-z0-9]{8})_[A-Za-z0-9]{40}$")]
    private static partial Regex Formato();

    /// <summary>Chave nova e o prefixo dela.</summary>
    public static (string Chave, string Prefixo) Gerar()
    {
        var prefixo = RandomNumberGenerator.GetString(Alfabeto, TamanhoPrefixo);
        var segredo = RandomNumberGenerator.GetString(Alfabeto, TamanhoSegredo);
        return ($"sge_{prefixo}_{segredo}", prefixo);
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
