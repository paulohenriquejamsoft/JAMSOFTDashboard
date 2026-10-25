using System.Buffers.Text;
using Jamsoft.DashBoards.Api.Modulos.Coletores;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Coletores;

[Trait("Categoria", "Unidade")]
public sealed class SegredoColetorTestes
{
    [Fact]
    public void Segredo_gerado_tem_43_caracteres_Base64Url_e_decodifica_para_32_bytes()
    {
        var segredo = SegredoColetor.Gerar();

        Assert.Equal(43, segredo.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", segredo);
        Assert.Equal(32, Base64Url.DecodeFromChars(segredo).Length);
    }

    [Fact]
    public void Dois_segredos_seguidos_diferem()
    {
        Assert.NotEqual(SegredoColetor.Gerar(), SegredoColetor.Gerar());
    }

    [Fact]
    public void Hash_tem_32_bytes_e_e_o_SHA256_do_segredo()
    {
        var segredo = SegredoColetor.Gerar();

        var hash = SegredoColetor.Hash(segredo);

        Assert.Equal(32, hash.Length);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(segredo)), hash);
    }

    [Fact]
    public void Comparacao_aceita_o_segredo_certo_e_recusa_um_com_1_caractere_trocado()
    {
        var segredo = SegredoColetor.Gerar();
        var hash = SegredoColetor.Hash(segredo);
        var trocado = (segredo[0] == 'A' ? 'B' : 'A') + segredo[1..];

        Assert.True(SegredoColetor.Confere(segredo, hash));
        Assert.False(SegredoColetor.Confere(trocado, hash));
    }
}
