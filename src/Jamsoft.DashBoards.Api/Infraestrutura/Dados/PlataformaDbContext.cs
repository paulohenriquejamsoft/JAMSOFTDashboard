using Jamsoft.DashBoards.Api.Infraestrutura.Dados.Convencoes;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados;

/// <summary>Entidade com dado de cliente: carrega a coluna TENANT_ID (AD-001).</summary>
public interface IPertenceAoTenant
{
    int TenantId { get; set; }
}

/// <summary>Banco da plataforma (SQL Server 2019). Mapeamentos em IEntityTypeConfiguration de cada módulo.</summary>
public sealed class PlataformaDbContext(DbContextOptions<PlataformaDbContext> options) : DbContext(options)
{
    /// <summary>Nível de compatibilidade do SQL Server 2019, alvo da plataforma.</summary>
    public const int CompatibilidadeSqlServer = 150;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlataformaDbContext).Assembly);
        NomesUpperSnake.Aplicar(modelBuilder);
    }

    public static void Configurar(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(CompatibilidadeSqlServer));
}

/// <summary>Usado só pelo dotnet ef (migrações); não conecta ao banco.</summary>
public sealed class PlataformaDbContextDesign : IDesignTimeDbContextFactory<PlataformaDbContext>
{
    public PlataformaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(options, "Server=.;Database=JamsoftDashBoards;Integrated Security=true;TrustServerCertificate=true");
        return new PlataformaDbContext(options.Options);
    }
}
