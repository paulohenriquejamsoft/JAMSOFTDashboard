namespace Jamsoft.DashBoards.Api.Seguranca;

/// <summary>Níveis de acesso (§7). Gravado no banco pelo nome.</summary>
public enum Perfil
{
    SuperAdmin,
    Admin,
    Consulta,
    /// <summary>Credencial de instalação do Coletor; nunca é um usuário do portal.</summary>
    Coletor,
}
