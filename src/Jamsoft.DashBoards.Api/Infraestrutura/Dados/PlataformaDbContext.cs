using System.Linq.Expressions;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados.Convencoes;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Autenticacao;
using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados;

/// <summary>Entidade com dado de cliente: carrega a coluna TENANT_ID (AD-001).</summary>
public interface IPertenceAoTenant
{
    int TenantId { get; set; }
}

/// <summary>
/// Banco da plataforma (SQL Server 2019). Mapeamentos em IEntityTypeConfiguration de cada módulo.
/// Toda entidade <see cref="IPertenceAoTenant"/> recebe o filtro nomeado <see cref="FiltroTenant"/>:
/// só enxerga linhas do tenant atual (ISO-02).
/// </summary>
public sealed class PlataformaDbContext(DbContextOptions<PlataformaDbContext> options, ITenantAtual tenantAtual)
    : DbContext(options)
{
    /// <summary>Nível de compatibilidade do SQL Server 2019, alvo da plataforma.</summary>
    public const int CompatibilidadeSqlServer = 150;

    /// <summary>Nome do filtro global por tenant (filtros nomeados do EF Core 10).</summary>
    public const string FiltroTenant = "Tenant";

    public ITenantAtual TenantAtual { get; } = tenantAtual;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();
    public DbSet<ExclusaoTenant> ExclusoesTenant => Set<ExclusaoTenant>();

    /// <summary>Lido a cada consulta: o EF troca a referência ao contexto pela instância em uso.</summary>
    private int TenantIdFiltro => TenantAtual.IdOuSemAcesso;

    private int _operacoesGlobais;

    /// <summary>Verdadeiro dentro de <see cref="OperacaoGlobal"/>.</summary>
    public bool EmOperacaoGlobal => _operacoesGlobais > 0;

    /// <summary>
    /// Libera, até o descarte do retorno, a gravação de entidades de qualquer tenant
    /// (<see cref="TenantSaveChangesInterceptor"/>). Só para SuperAdmin e a rotina de exclusão.
    /// Não desliga o filtro de leitura: use <c>IgnoreQueryFilters([FiltroTenant])</c> quando preciso.
    /// </summary>
    public IDisposable OperacaoGlobal()
    {
        _operacoesGlobais++;
        return new FimOperacaoGlobal(this);
    }

    private sealed class FimOperacaoGlobal(PlataformaDbContext contexto) : IDisposable
    {
        private bool _encerrada;

        public void Dispose()
        {
            if (_encerrada) return;
            _encerrada = true;
            contexto._operacoesGlobais--;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlataformaDbContext).Assembly);
        NomesUpperSnake.Aplicar(modelBuilder);
        AplicarFiltroTenant(modelBuilder);
    }

    private void AplicarFiltroTenant(ModelBuilder modelBuilder)
    {
        var tipos = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(IPertenceAoTenant).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        foreach (var tipo in tipos)
        {
            // e => e.TenantId == this.TenantIdFiltro
            var e = Expression.Parameter(tipo, "e");
            var filtro = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(e, nameof(IPertenceAoTenant.TenantId)),
                    Expression.Property(Expression.Constant(this), nameof(TenantIdFiltro))),
                e);
            modelBuilder.Entity(tipo).HasQueryFilter(FiltroTenant, filtro);
        }
    }

    public static void Configurar(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(CompatibilidadeSqlServer))
            .AddInterceptors(TenantSaveChangesInterceptor.Instancia);
}

public static class DadosPlataformaExtensoes
{
    /// <summary>Registra o DbContext da plataforma e o <see cref="ITenantAtual"/> que ele usa.</summary>
    public static IServiceCollection AddDadosPlataforma(this IServiceCollection services, string? connectionString)
    {
        services.AddTenantAtual();
        services.AddDbContext<PlataformaDbContext>(o => PlataformaDbContext.Configurar(o, connectionString));
        return services;
    }
}

/// <summary>Usado só pelo dotnet ef (migrações); não conecta ao banco.</summary>
public sealed class PlataformaDbContextDesign : IDesignTimeDbContextFactory<PlataformaDbContext>
{
    public PlataformaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(options, "Server=.;Database=JamsoftDashBoards;Integrated Security=true;TrustServerCertificate=true");
        return new PlataformaDbContext(options.Options, TenantSemAcesso.Instancia);
    }
}
