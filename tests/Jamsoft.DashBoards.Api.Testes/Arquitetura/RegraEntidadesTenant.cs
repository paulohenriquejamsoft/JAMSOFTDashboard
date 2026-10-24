using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Jamsoft.DashBoards.Api.Testes.Arquitetura;

/// <summary>
/// Regra das entidades com <c>TenantId</c> (design, Risks &amp; Concerns): toda entidade com a propriedade
/// implementa <see cref="IPertenceAoTenant"/> ou está em <see cref="Excecoes"/>; e toda entidade com
/// <c>TenantId</c>, menos <see cref="ExclusaoTenant"/>, está no <see cref="PlanoExclusao"/>.
/// </summary>
public static class RegraEntidadesTenant
{
    /// <summary>Fora do filtro global, de propósito: login antes de haver tenant (USUARIO, REFRESH_TOKEN) e o job
    /// de exclusão, que sobrevive ao tenant.</summary>
    public static readonly IReadOnlyList<Type> Excecoes = [typeof(Usuario), typeof(RefreshToken), typeof(ExclusaoTenant)];

    public static IReadOnlyList<string> Violacoes(IModel modelo)
    {
        var plano = PlanoExclusao.De(modelo).Tabelas.ToHashSet(StringComparer.Ordinal);
        var violacoes = new List<string>();
        foreach (var entidade in modelo.GetEntityTypes())
        {
            var tipo = entidade.ClrType;
            if (tipo.GetProperty(nameof(IPertenceAoTenant.TenantId)) is null) continue;

            if (!typeof(IPertenceAoTenant).IsAssignableFrom(tipo) && !Excecoes.Contains(tipo))
                violacoes.Add($"{tipo.Name} tem TenantId sem implementar IPertenceAoTenant.");
            if (tipo != typeof(ExclusaoTenant) && !plano.Contains(entidade.GetTableName()!))
                violacoes.Add($"{tipo.Name} tem TenantId e não está no PlanoExclusao.");
        }
        return violacoes;
    }
}
