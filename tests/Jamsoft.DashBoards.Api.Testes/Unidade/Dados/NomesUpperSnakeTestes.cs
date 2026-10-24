using Jamsoft.DashBoards.Api.Infraestrutura.Dados.Convencoes;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Dados;

[Trait("Categoria", "Unidade")]
public sealed class NomesUpperSnakeTestes
{
    [Theory]
    [InlineData("Nome", "NOME")]                     // palavra simples
    [InlineData("CriadoEm", "CRIADO_EM")]            // composta
    [InlineData("Cnpj", "CNPJ")]                     // sigla escrita em PascalCase
    [InlineData("IPOrigem", "IP_ORIGEM")]            // sigla em maiúsculas seguida de palavra
    [InlineData("TenantId", "TENANT_ID")]            // sufixo Id
    [InlineData("IdPublico", "ID_PUBLICO")]          // prefixo Id
    [InlineData("Linha2Endereco", "LINHA2_ENDERECO")] // número
    public void Converter_gera_UPPER_SNAKE(string nome, string esperado)
    {
        Assert.Equal(esperado, NomesUpperSnake.Converter(nome));
    }

    [Fact]
    public void Aplicar_nomeia_tabela_no_singular_e_colunas_em_UPPER_SNAKE()
    {
        using var contexto = new ContextoExemplo();
        var entidade = contexto.Model.FindEntityType(typeof(RefreshTokenExemplo))!;

        Assert.Equal("REFRESH_TOKEN_EXEMPLO", entidade.GetTableName());
        Assert.Equal("TENANT_ID", entidade.FindProperty(nameof(RefreshTokenExemplo.TenantId))!.GetColumnName());
        Assert.Equal("CRIADO_EM", entidade.FindProperty(nameof(RefreshTokenExemplo.CriadoEm))!.GetColumnName());
    }

    [Fact]
    public void Aplicar_mantem_nome_de_tabela_definido_explicitamente()
    {
        using var contexto = new ContextoExemplo();
        var entidade = contexto.Model.FindEntityType(typeof(RegistroExemplo))!;

        Assert.Equal("AUDITORIA_EXEMPLO", entidade.GetTableName());
        Assert.Equal("OCORRIDO_EM", entidade.FindProperty(nameof(RegistroExemplo.OcorridoEm))!.GetColumnName());
    }

    private sealed class RegistroExemplo
    {
        public long Id { get; set; }
        public DateTime OcorridoEm { get; set; }
    }

    private sealed class RefreshTokenExemplo
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public DateTime CriadoEm { get; set; }
    }

    private sealed class ContextoExemplo : DbContext
    {
        public DbSet<RefreshTokenExemplo> Tokens => Set<RefreshTokenExemplo>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlServer("Server=nao-conecta");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RefreshTokenExemplo>();
            modelBuilder.Entity<RegistroExemplo>().ToTable("AUDITORIA_EXEMPLO");
            NomesUpperSnake.Aplicar(modelBuilder);
        }
    }
}
