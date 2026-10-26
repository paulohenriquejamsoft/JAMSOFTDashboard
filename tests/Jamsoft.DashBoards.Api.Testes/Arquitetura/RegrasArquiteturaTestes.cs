using System.Text.RegularExpressions;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados.Convencoes;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Arquitetura;

[Trait("Categoria", "Arquitetura")]
public sealed class RegrasArquiteturaTestes
{
    /// <summary>Namespaces que podem desligar o isolamento (design, Risks &amp; Concerns).</summary>
    private static readonly string[] NamespacesComBypass =
    [
        "Jamsoft.DashBoards.Api.Modulos.Tenants",
        "Jamsoft.DashBoards.Api.Modulos.Auditoria",
        "Jamsoft.DashBoards.Api.Modulos.Exclusao",
        "Jamsoft.DashBoards.Api.Modulos.Autenticacao",
        // Admin de coletores: COLETOR_TENANT é do tenant, mas a listagem atravessa tenants (design, Risks).
        "Jamsoft.DashBoards.Api.Modulos.Coletores",
        // Chaves MCP: emitidas pelo CLI, sem usuário no contexto, para o tenant escolhido pelo operador.
        "Jamsoft.DashBoards.Api.Modulos.Mcp",
    ];

    /// <summary>Infraestrutura que define <c>OperacaoGlobal</c> e aplica o filtro global.</summary>
    private const string ArquivoQueDefine = "Infraestrutura/Dados/PlataformaDbContext.cs";

    [Fact]
    public void Toda_rota_declara_policy_ou_AllowAnonymous()
    {
        // Sem IAuthorizeData nem IAllowAnonymous a rota cairia só na policy de fallback, que não conta como explícita.
        var semDeclaracao = RotasDaApi.Todas
            .Where(r => r.Metadata.GetMetadata<IAllowAnonymous>() is null && r.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(RotasDaApi.Descrever).ToList();

        Assert.NotEmpty(RotasDaApi.Todas);
        Assert.Empty(semDeclaracao);
    }

    [Fact]
    public void Rotas_de_api_admin_exigem_a_policy_SuperAdmin()
    {
        var admin = RotasDaApi.Todas.Where(r => r.RoutePattern.RawText!.StartsWith("/api/admin", StringComparison.Ordinal)).ToList();
        var semSuperAdmin = admin
            .Where(r => r.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || !r.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == Policies.SuperAdmin))
            .Select(RotasDaApi.Descrever).ToList();

        Assert.NotEmpty(admin);
        Assert.Empty(semSuperAdmin);
    }

    [Fact]
    public void IgnoreQueryFilters_e_OperacaoGlobal_so_aparecem_nos_modulos_autorizados()
    {
        var projeto = Path.Combine(DiretorioDaSolucao(), "src", "Jamsoft.DashBoards.Api");
        var uso = new Regex(@"\b(IgnoreQueryFilters|OperacaoGlobal)\s*\(");
        var declaracaoNamespace = new Regex(@"^namespace\s+([\w.]+)", RegexOptions.Multiline);
        var separador = Path.DirectorySeparatorChar;
        var arquivos = Directory.EnumerateFiles(projeto, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separador}obj{separador}") && !f.Contains($"{separador}bin{separador}"))
            .ToList();

        var indevidos = new List<string>();
        foreach (var arquivo in arquivos)
        {
            var relativo = Path.GetRelativePath(projeto, arquivo).Replace(Path.DirectorySeparatorChar, '/');
            var texto = File.ReadAllText(arquivo);
            if (relativo == ArquivoQueDefine || !uso.IsMatch(texto)) continue;
            var ns = declaracaoNamespace.Match(texto).Groups[1].Value;
            if (!NamespacesComBypass.Any(p => ns == p || ns.StartsWith(p + ".", StringComparison.Ordinal)))
                indevidos.Add($"{relativo} ({ns})");
        }

        Assert.Contains(arquivos, f => f.EndsWith("PlataformaDbContext.cs", StringComparison.Ordinal));
        Assert.Empty(indevidos);
    }

    private static string DiretorioDaSolucao()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jamsoft.DashBoards.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Jamsoft.DashBoards.slnx não encontrado.");
    }

    [Fact]
    public void Toda_entidade_com_TenantId_implementa_IPertenceAoTenant_e_esta_no_plano_de_exclusao()
    {
        using var contexto = new PlataformaDbContextDesign().CreateDbContext([]);

        Assert.Empty(RegraEntidadesTenant.Violacoes(contexto.Model));
    }

    private static List<string> TabelasDoPlano()
    {
        using var contexto = new PlataformaDbContextDesign().CreateDbContext([]);
        return PlanoExclusao.De(contexto.Model).Tabelas.ToList();
    }

    [Fact]
    public void Plano_de_exclusao_apaga_vendas_e_lotes_antes_da_empresa_e_a_empresa_antes_da_origem()
    {
        var tabelas = TabelasDoPlano();
        int Posicao(string tabela)
        {
            Assert.Contains(tabela, tabelas);
            return tabelas.IndexOf(tabela);
        }

        // FKs: VENDA → EMPRESA → ORIGEM_SINCRONIZACAO e LOTE_INGESTAO → ORIGEM_SINCRONIZACAO (filhos antes dos pais).
        Assert.True(Posicao("VENDA") < Posicao("EMPRESA"), string.Join(", ", tabelas));
        Assert.True(Posicao("EMPRESA") < Posicao("ORIGEM_SINCRONIZACAO"), string.Join(", ", tabelas));
        Assert.True(Posicao("LOTE_INGESTAO") < Posicao("ORIGEM_SINCRONIZACAO"), string.Join(", ", tabelas));
    }

    [Fact]
    public void Plano_de_exclusao_apaga_COLETOR_TENANT_antes_de_TENANT_e_deixa_COLETOR_fora()
    {
        var tabelas = TabelasDoPlano();

        Assert.Contains("COLETOR_TENANT", tabelas);
        Assert.True(tabelas.IndexOf("COLETOR_TENANT") < tabelas.IndexOf("TENANT"), string.Join(", ", tabelas));
        Assert.Equal("TENANT", tabelas[^1]);
        // COLETOR não pertence a um tenant: atende vários e sobrevive à exclusão de um deles.
        Assert.DoesNotContain("COLETOR", tabelas);
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
