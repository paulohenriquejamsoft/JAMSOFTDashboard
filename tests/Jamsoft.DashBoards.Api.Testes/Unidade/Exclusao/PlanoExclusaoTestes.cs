using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Exclusao;

[Trait("Categoria", "Unidade")]
public sealed class PlanoExclusaoTestes
{
    private static PlanoExclusao Plano()
    {
        using var contexto = new PlataformaDbContextDesign().CreateDbContext([]);
        return PlanoExclusao.De(contexto.Model);
    }

    [Fact]
    public void REFRESH_TOKEN_vem_antes_de_USUARIO()
    {
        var tabelas = Plano().Tabelas.ToList();

        Assert.Contains("REFRESH_TOKEN", tabelas);
        Assert.Contains("USUARIO", tabelas);
        Assert.True(tabelas.IndexOf("REFRESH_TOKEN") < tabelas.IndexOf("USUARIO"), string.Join(", ", tabelas));
    }

    [Fact]
    public void USUARIO_e_EMPRESA_vem_antes_de_TENANT_que_fecha_o_plano()
    {
        var plano = Plano();
        var tabelas = plano.Tabelas.ToList();

        Assert.True(tabelas.IndexOf("USUARIO") is >= 0 and var u && u < tabelas.IndexOf("TENANT"));
        Assert.True(tabelas.IndexOf("EMPRESA") is >= 0 and var e && e < tabelas.IndexOf("TENANT"));
        // TENANT sai pelo ID, por último; as demais sempre pelo TENANT_ID (DEL-07).
        Assert.Equal(new EtapaExclusao("TENANT", "ID"), plano.Etapas[^1]);
        Assert.All(plano.Etapas.SkipLast(1), etapa => Assert.Equal("TENANT_ID", etapa.ColunaTenant));
        Assert.Single(tabelas, "TENANT");
    }

    [Fact]
    public void AUDITORIA_e_EXCLUSAO_TENANT_ficam_fora_do_plano()
    {
        var tabelas = Plano().Tabelas.ToList();

        Assert.DoesNotContain("AUDITORIA", tabelas);
        Assert.DoesNotContain("EXCLUSAO_TENANT", tabelas);
    }
}
