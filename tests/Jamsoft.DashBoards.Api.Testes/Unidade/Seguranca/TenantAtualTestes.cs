using System.Security.Claims;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Http;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Seguranca;

[Trait("Categoria", "Unidade")]
public sealed class TenantAtualTestes
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private const int IdTenantA = 10;
    private const int IdTenantB = 20;

    private sealed class ResolvedorFixo : IResolvedorTenant
    {
        public int? ObterId(Guid idPublico) =>
            idPublico == TenantA ? IdTenantA : idPublico == TenantB ? IdTenantB : null;
    }

    private static TenantAtual Criar(Perfil? perfil, Guid? tenantToken = null, string? cabecalho = null,
        Guid? usuario = null, bool trocarSenha = false)
    {
        var http = new DefaultHttpContext();
        if (perfil is not null)
        {
            var claims = new List<Claim>
            {
                new(TiposClaim.Perfil, perfil.Value.ToString()),
                new(TiposClaim.Usuario, (usuario ?? Guid.NewGuid()).ToString()),
                new(TiposClaim.TrocarSenha, trocarSenha ? "true" : "false"),
            };
            if (tenantToken is not null) claims.Add(new(TiposClaim.Tenant, tenantToken.Value.ToString()));
            http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Teste"));
        }
        if (cabecalho is not null) http.Request.Headers[TenantAtual.CabecalhoTenant] = cabecalho;
        return new TenantAtual(new HttpContextAccessor { HttpContext = http }, new ResolvedorFixo());
    }

    [Fact]
    public void Admin_sem_cabecalho_usa_o_tenant_do_token_e_le_as_claims()
    {
        var usuario = Guid.NewGuid();
        ITenantAtual atual = Criar(Perfil.Admin, TenantA, usuario: usuario, trocarSenha: true);

        Assert.Equal(Perfil.Admin, atual.Perfil);
        Assert.Equal(usuario, atual.UsuarioIdPublico);
        Assert.True(atual.TrocaSenhaPendente);
        Assert.Equal(TenantA, atual.IdPublico);
        Assert.Equal(IdTenantA, atual.Id);
        Assert.Equal(IdTenantA, atual.IdOuSemAcesso);
        Assert.False(atual.Divergente);
    }

    [Fact]
    public void Admin_com_cabecalho_igual_ao_token_ignora_o_cabecalho()
    {
        ITenantAtual atual = Criar(Perfil.Admin, TenantA, cabecalho: TenantA.ToString());

        Assert.Equal(IdTenantA, atual.Id);
        Assert.False(atual.Divergente);
        Assert.False(atual.TrocaSenhaPendente);
    }

    [Fact]
    public void Admin_com_cabecalho_diferente_fica_divergente_e_mantem_o_tenant_do_token()
    {
        ITenantAtual atual = Criar(Perfil.Admin, TenantA, cabecalho: TenantB.ToString());

        Assert.True(atual.Divergente);
        Assert.Equal(TenantA, atual.IdPublico);
        Assert.Equal(IdTenantA, atual.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Consulta_segue_a_mesma_regra_do_Admin(bool cabecalhoDiferente)
    {
        var cabecalho = cabecalhoDiferente ? TenantB : TenantA;
        ITenantAtual atual = Criar(Perfil.Consulta, TenantA, cabecalho: cabecalho.ToString());

        Assert.Equal(cabecalhoDiferente, atual.Divergente);
        Assert.Equal(IdTenantA, atual.Id);
    }

    [Fact]
    public void SuperAdmin_com_X_Tenant_usa_o_tenant_selecionado()
    {
        ITenantAtual atual = Criar(Perfil.SuperAdmin, cabecalho: TenantB.ToString());

        Assert.Equal(TenantB, atual.IdPublico);
        Assert.Equal(IdTenantB, atual.Id);
        Assert.False(atual.Divergente);
    }

    [Fact]
    public void SuperAdmin_sem_X_Tenant_fica_sem_tenant()
    {
        ITenantAtual atual = Criar(Perfil.SuperAdmin);

        Assert.Equal(Perfil.SuperAdmin, atual.Perfil);
        Assert.Null(atual.IdPublico);
        Assert.Null(atual.Id);
        Assert.Equal(-1, atual.IdOuSemAcesso);
    }

    [Fact]
    public void X_Tenant_mal_formatado_e_marcado_e_nao_seleciona_tenant()
    {
        ITenantAtual atual = Criar(Perfil.SuperAdmin, cabecalho: "nao-e-guid");

        Assert.True(atual.CabecalhoTenantInvalido);
        Assert.Null(atual.Id);
        Assert.Equal(-1, atual.IdOuSemAcesso);
    }

    [Fact]
    public void Sem_autenticacao_nao_tem_perfil_nem_acesso()
    {
        ITenantAtual atual = Criar(perfil: null, cabecalho: TenantA.ToString());

        Assert.Null(atual.Perfil);
        Assert.Null(atual.UsuarioIdPublico);
        Assert.Null(atual.Id);
        Assert.Equal(-1, atual.IdOuSemAcesso);
    }
}
