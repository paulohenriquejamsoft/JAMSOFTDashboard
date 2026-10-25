using System.Security.Cryptography;
using System.Text;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;

namespace Jamsoft.DashBoards.Coletor.Testes.Unidade.Configuracao;

/// <summary>Segredos do Coletor cifrados com DPAPI, nunca em texto puro (CFG-08).</summary>
[Trait("Categoria", "Unidade")]
public sealed class ProtetorSegredosTestes
{
    private const string Senha = "S3nh@-do-ERP_ção";

    [Fact]
    public void Ida_e_volta_preserva_o_texto()
    {
        var cifrado = ProtetorSegredos.Cifrar(Senha);

        Assert.Equal(Senha, ProtetorSegredos.Decifrar(cifrado));
    }

    [Fact]
    public void Texto_cifrado_nao_contem_o_original()
    {
        var cifrado = ProtetorSegredos.Cifrar(Senha);

        Assert.DoesNotContain(Senha, cifrado, StringComparison.Ordinal);
        var bytes = Convert.FromBase64String(cifrado);
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Senha)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(Senha)));
    }

    [Fact]
    public void Cifrado_adulterado_lanca_CryptographicException()
    {
        var bytes = Convert.FromBase64String(ProtetorSegredos.Cifrar(Senha));
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => ProtetorSegredos.Decifrar(Convert.ToBase64String(bytes)));
    }
}
