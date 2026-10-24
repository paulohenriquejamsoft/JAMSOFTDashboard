using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Jamsoft.DashBoards.Api.Modulos.Exclusao;

/// <summary>Uma tabela do plano: apaga as linhas em que <see cref="ColunaTenant"/> é o tenant alvo.</summary>
public sealed record EtapaExclusao(string Tabela, string ColunaTenant);

/// <summary>
/// Tabelas apagadas na exclusão de um tenant (DEL-04, DEL-07), montadas a partir do modelo EF: toda entidade
/// <see cref="IPertenceAoTenant"/> mais <see cref="ComTenantForaDoFiltro"/> (USUARIO e REFRESH_TOKEN, cujo
/// TENANT_ID é nulo para o SuperAdmin, que nunca é afetado). A ordem segue as FKs, filhos antes dos pais, e TENANT
/// (pelo ID) vem sempre por último. AUDITORIA e EXCLUSAO_TENANT ficam fora: precisam sobreviver ao tenant.
/// Tabelas novas com <see cref="IPertenceAoTenant"/> entram sozinhas.
/// </summary>
public sealed class PlanoExclusao
{
    /// <summary>Entidades com TENANT_ID fora do filtro global que também são apagadas.</summary>
    public static readonly IReadOnlyList<Type> ComTenantForaDoFiltro = [typeof(Usuario), typeof(RefreshToken)];

    private PlanoExclusao(IReadOnlyList<EtapaExclusao> etapas) => Etapas = etapas;

    /// <summary>Em ordem de execução; a última é TENANT.</summary>
    public IReadOnlyList<EtapaExclusao> Etapas { get; }

    public IEnumerable<string> Tabelas => Etapas.Select(e => e.Tabela);

    public static PlanoExclusao De(IModel modelo)
    {
        var tenant = modelo.FindEntityType(typeof(Tenant))
            ?? throw new InvalidOperationException("Modelo sem a entidade Tenant.");

        var entidades = modelo.GetEntityTypes()
            .Where(t => !t.IsOwned() && (typeof(IPertenceAoTenant).IsAssignableFrom(t.ClrType)
                || ComTenantForaDoFiltro.Contains(t.ClrType)))
            .ToList();

        var etapas = OrdenarFilhosAntesDosPais(entidades)
            .Select(t => new EtapaExclusao(Tabela(t), Coluna(t, nameof(IPertenceAoTenant.TenantId))))
            .Append(new EtapaExclusao(Tabela(tenant), Coluna(tenant, nameof(Tenant.Id))))
            .ToList();
        return new PlanoExclusao(etapas);
    }

    /// <summary>
    /// Ordem topológica: uma tabela só sai depois de todas as que a referenciam. Empates por nome, para a ordem
    /// ser estável. FKs para tabelas fora da lista (como TENANT) não contam.
    /// </summary>
    private static List<IEntityType> OrdenarFilhosAntesDosPais(List<IEntityType> entidades)
    {
        // Para cada entidade, as entidades da lista que a referenciam (filhos) e ainda não saíram.
        var filhosPendentes = entidades.ToDictionary(e => e, _ => new HashSet<IEntityType>());
        foreach (var filho in entidades)
            foreach (var fk in filho.GetForeignKeys())
                if (fk.PrincipalEntityType != filho && filhosPendentes.TryGetValue(fk.PrincipalEntityType, out var filhos))
                    filhos.Add(filho);

        var ordem = new List<IEntityType>();
        while (filhosPendentes.Count > 0)
        {
            var proxima = filhosPendentes.Where(p => p.Value.Count == 0).Select(p => p.Key)
                .OrderBy(Tabela, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("Ciclo de FKs entre tabelas do tenant.");
            ordem.Add(proxima);
            filhosPendentes.Remove(proxima);
            foreach (var filhos in filhosPendentes.Values) filhos.Remove(proxima);
        }
        return ordem;
    }

    private static string Tabela(IEntityType entidade) =>
        entidade.GetTableName() ?? throw new InvalidOperationException($"{entidade.ClrType.Name} sem tabela.");

    private static string Coluna(IEntityType entidade, string propriedade) =>
        entidade.FindProperty(propriedade)?.GetColumnName()
        ?? throw new InvalidOperationException($"{entidade.ClrType.Name} sem a propriedade {propriedade}.");
}
