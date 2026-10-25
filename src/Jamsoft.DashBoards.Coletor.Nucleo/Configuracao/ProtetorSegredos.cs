using System.Security.Cryptography;
using System.Text;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;

/// <summary>
/// Cifra senhas de banco e o <c>client_secret</c> com DPAPI escopo <c>LocalMachine</c> e entropia fixa do
/// produto (CFG-08, AD-016). O resultado vai em Base64 no <c>config.json</c>.
/// </summary>
public static class ProtetorSegredos
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("Jamsoft.DashBoards.Coletor/segredos/v1");

    public static string Cifrar(string texto) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), Entropia, DataProtectionScope.LocalMachine));

    /// <exception cref="CryptographicException">Cifrado adulterado ou de outra máquina.</exception>
    public static string Decifrar(string cifrado) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cifrado), Entropia, DataProtectionScope.LocalMachine));
}
