using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados.Convencoes;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Arquitetura;

[Trait("Categoria", "Arquitetura")]
public sealed class RegrasArquiteturaTestes
{
    [Fact]
    public void Toda_entidade_com_TenantId_implementa_IPertenceAoTenant_e_esta_no_plano_de_exclusao()
    {
        using var contexto = new PlataformaDbContextDesign().CreateDbContext([]);

        Assert.Empty(RegraEntidadesTenant.Violacoes(contexto.Model));
    }

    [Fact]
    public void Regra_de_entidades_com_TenantId_falha_com_entidade_sem_IPertenceAoTenant()
    {
        using var contexto = new ModeloComEntidadeIrregular();

        var violacoes = RegraEntidadesTenant.Violacoes(contexto.Model);

        // Só a entidade irregular é apontada, pelas duas regras; a que implementa a interface entra no plano.
        Assert.Equal(
            [
                $"{nameof(VendaSemInterface)} tem TenantId sem implementar IPertenceAoTenant.",
                $"{nameof(VendaSemInterface)} tem TenantId e não está no PlanoExclusao.",
            ],
            violacoes);
    }

    private sealed class VendaSemInterface
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
    }

    private sealed class ItemComInterface : IPertenceAoTenant
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
    }

    /// <summary>Modelo real da plataforma mais uma entidade de tenant esquecida e uma correta.</summary>
    private sealed class ModeloComEntidadeIrregular : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlServer("Server=nao-conecta");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlataformaDbContext).Assembly);
            modelBuilder.Entity<VendaSemInterface>().HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId);
            modelBuilder.Entity<ItemComInterface>().HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId);
            NomesUpperSnake.Aplicar(modelBuilder);
        }
    }
}
