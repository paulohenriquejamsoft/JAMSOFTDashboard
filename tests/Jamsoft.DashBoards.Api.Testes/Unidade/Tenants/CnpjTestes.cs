using Jamsoft.DashBoards.Api.Modulos.Tenants;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Tenants;

[Trait("Categoria", "Unidade")]
public sealed class CnpjTestes
{
    // Dígitos verificadores calculados à mão para a base 12.345.678/0001 (valor fictício).
    private const string Valido = "12345678000195";

    [Fact]
    public void CNPJ_valido_sem_mascara_e_aceito()
    {
        Assert.True(Cnpj.TentarNormalizar(Valido, out var normalizado));
        Assert.Equal(Valido, normalizado);
    }

    [Fact]
    public void CNPJ_valido_com_mascara_e_aceito_e_guardado_so_com_digitos()
    {
        Assert.True(Cnpj.TentarNormalizar(" 12.345.678/0001-95 ", out var normalizado));
        Assert.Equal(Valido, normalizado);
    }

    [Theory]
    [InlineData("12345678000194")] // segundo dígito errado
    [InlineData("12345678000185")] // primeiro dígito errado
    public void Digito_verificador_errado_e_recusado(string cnpj)
    {
        Assert.False(Cnpj.TentarNormalizar(cnpj, out var normalizado));
        Assert.Equal("", normalizado);
    }

    [Theory]
    [InlineData("1234567800019")] // 13 dígitos
    [InlineData("123456780001950")] // 15 dígitos
    [InlineData("")]
    public void Tamanho_errado_e_recusado(string cnpj)
    {
        Assert.False(Cnpj.TentarNormalizar(cnpj, out _));
    }

    [Theory]
    [InlineData("00000000000000")]
    [InlineData("11111111111111")]
    public void Sequencia_repetida_e_recusada_mesmo_com_digitos_verificadores_coerentes(string cnpj)
    {
        Assert.False(Cnpj.TentarNormalizar(cnpj, out _));
    }

    [Fact]
    public void Letras_sao_recusadas()
    {
        Assert.False(Cnpj.TentarNormalizar("12A45678000195", out _));
        Assert.False(Cnpj.TentarNormalizar("12345678000195x", out _));
    }
}
