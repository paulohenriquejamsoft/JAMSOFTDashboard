using Jamsoft.DashBoards.Api.Seguranca;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Tenant atual fixo, para testar o DbContext e serviços fora de uma requisição HTTP.</summary>
public sealed class TenantFixo(int? id, Perfil? perfil = Perfil.Admin, Guid? idPublico = null, Guid? usuarioIdPublico = null)
    : ITenantAtual
{
    public Perfil? Perfil { get; } = perfil;
    public Guid? UsuarioIdPublico { get; } = usuarioIdPublico;
    public bool TrocaSenhaPendente => false;
    public int? Id { get; } = id;
    public Guid? IdPublico { get; } = idPublico;
    public bool Divergente => false;
    public bool CabecalhoTenantInvalido => false;
}
