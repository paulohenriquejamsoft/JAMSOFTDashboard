using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Jamsoft.DashBoards.Api.Modulos.Coletores;

/// <summary>
/// <c>client_secret</c> do Coletor (COL-01, COL-02): 32 bytes aleatórios em Base64Url (43 caracteres).
/// Só o SHA-256 vai para o banco; a conferência compara em tempo constante.
/// </summary>
public static class SegredoColetor
{
    public const int BytesAleatorios = 32;

    public static string Gerar() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(BytesAleatorios));

    public static byte[] Hash(string segredo) => SHA256.HashData(Encoding.UTF8.GetBytes(segredo));

    public static bool Confere(string segredo, byte[] hash) => CryptographicOperations.FixedTimeEquals(Hash(segredo), hash);
}
