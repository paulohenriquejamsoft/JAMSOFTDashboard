using Jamsoft.DashBoards.Api.Cli;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Contratos.Mcp;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Cli;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ComandosCliTestes(BancoDeTestes banco) : IAsyncLifetime
{
    public Task InitializeAsync() => banco.MigrarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static IConfiguration Configuracao(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Plataforma"] = connectionString })
            .Build();

    private static (int? Codigo, string Saida, string Erro) Executar(string connectionString, params string[] args)
    {
        using var saida = new StringWriter();
        using var erro = new StringWriter();
        var codigo = ComandosCli.Executar(args, Configuracao(connectionString), saida, erro);
        return (codigo, saida.ToString(), erro.ToString());
    }

    [Fact]
    public async Task Migrar_em_banco_vazio_cria_todas_as_tabelas()
    {
        var vazio = await banco.CriarBancoVazioAsync();

        var (codigo, _, _) = Executar(vazio, "migrar");

        Assert.Equal(0, codigo);
        await using var contexto = banco.CriarContexto();
        var esperadas = contexto.Model.GetEntityTypes().Select(t => t.GetTableName()!).Order().ToList();
        Assert.Contains("USUARIO", esperadas);

        await using var conexao = new SqlConnection(vazio);
        await conexao.OpenAsync();
        await using var comando = new SqlCommand(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME <> '__EFMigrationsHistory'",
            conexao);
        var criadas = new List<string>();
        await using (var leitor = await comando.ExecuteReaderAsync())
            while (await leitor.ReadAsync()) criadas.Add(leitor.GetString(0));
        Assert.Equal(esperadas, criadas.Order().ToList());
    }

    [Fact]
    public async Task Criar_superadmin_cria_usuario_SuperAdmin_sem_tenant_com_troca_pendente_e_mostra_a_senha_uma_vez()
    {
        var email = $"{Guid.NewGuid():N}@Jamsoft.com.br";

        var (codigo, saida, _) = Executar(banco.ConnectionString, "criar-superadmin", "--email", $"  {email} ", "--nome", "Suporte");

        Assert.Equal(0, codigo);
        await using var contexto = banco.CriarContexto();
        var usuario = await contexto.Usuarios.AsNoTracking().SingleAsync(u => u.Email == email.ToLowerInvariant());
        Assert.Equal(Perfil.SuperAdmin, usuario.Perfil);
        Assert.Null(usuario.TenantId);
        Assert.True(usuario.TrocarSenha);
        Assert.True(usuario.Ativo);
        Assert.Equal("Suporte", usuario.Nome);

        // A senha exibida é a que confere com o hash gravado, e aparece uma única vez.
        var senha = saida.Split('\n').Select(l => l.Trim()).Single(l =>
            l.Length >= 16 && new PasswordHasher<Usuario>().VerifyHashedPassword(usuario, usuario.SenhaHash, l)
                != PasswordVerificationResult.Failed);
        Assert.True(senha.Any(char.IsLetter) && senha.Any(char.IsDigit));
        Assert.Equal(1, saida.Split(senha).Length - 1);
    }

    [Fact]
    public async Task Criar_superadmin_com_email_repetido_falha_com_mensagem_clara_e_nao_cria_outro()
    {
        var existente = await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin);

        var (codigo, saida, erro) = Executar(banco.ConnectionString,
            "criar-superadmin", "--email", existente.Email.ToUpperInvariant(), "--nome", "Outro");

        Assert.Equal(1, codigo);
        Assert.Contains($"Já existe um usuário com o e-mail {existente.Email}", erro);
        Assert.Empty(saida);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(1, await contexto.Usuarios.CountAsync(u => u.Email == existente.Email));
    }

    /// <summary>O comando confere os dígitos verificadores do CNPJ; o <see cref="Dados.NovoTenantAsync"/> não os gera.</summary>
    private async Task<Tenant> TenantComCnpjValidoAsync(bool ativo = true)
    {
        await using var contexto = banco.CriarContexto();
        var tenant = new Tenant { Nome = "Tenant de teste", Cnpj = Dados.CnpjValido(), Ativo = ativo };
        contexto.Tenants.Add(tenant);
        await contexto.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task Gerar_chave_mcp_grava_so_o_hash_para_o_tenant_do_cnpj_e_mostra_a_chave_uma_vez()
    {
        var tenant = await TenantComCnpjValidoAsync();
        var cnpjComMascara = Convert.ToUInt64(tenant.Cnpj).ToString(@"00\.000\.000\/0000\-00");

        var (codigo, saida, _) = Executar(banco.ConnectionString,
            "gerar-chave-mcp", "--cnpj", cnpjComMascara, "--descricao", "Claude - Painéis", "--expira", "2027-12-31");

        Assert.Equal(0, codigo);
        var chave = saida.Split('\n').Select(l => l.Trim()).Single(l => FormatoChaveMcp.TentarLerPrefixo(l, out _));
        FormatoChaveMcp.TentarLerPrefixo(chave, out var prefixo);
        Assert.Equal(1, saida.Split(chave).Length - 1);

        await using var contexto = banco.CriarContexto();
        var gravada = await contexto.ChavesMcp.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant]).AsNoTracking()
            .SingleAsync(c => c.Prefixo == prefixo);
        Assert.Equal(tenant.Id, gravada.TenantId);
        Assert.Equal(FormatoChaveMcp.Hash(chave), gravada.Hash);
        Assert.Equal("Claude - Painéis", gravada.Descricao);
        Assert.True(gravada.Ativo);
        // --expira é inclusivo: vale até o fim do dia em São Paulo (−03:00).
        Assert.Equal(new DateTime(2028, 1, 1, 3, 0, 0), gravada.ExpiraEm);
    }

    [Fact]
    public async Task Gerar_chave_mcp_recusa_tenant_inativo_ou_inexistente_sem_gravar()
    {
        var inativo = await TenantComCnpjValidoAsync(ativo: false);

        var (codigoInativo, saidaInativo, erroInativo) = Executar(banco.ConnectionString,
            "gerar-chave-mcp", "--cnpj", inativo.Cnpj, "--descricao", "Teste");
        var (codigoInexistente, _, erroInexistente) = Executar(banco.ConnectionString,
            "gerar-chave-mcp", "--cnpj", Dados.CnpjValido(), "--descricao", "Teste");

        Assert.Equal(1, codigoInativo);
        Assert.Contains("inativo", erroInativo);
        Assert.Empty(saidaInativo);
        Assert.Equal(1, codigoInexistente);
        Assert.Contains("Nenhum tenant", erroInexistente);
        await using var contexto = banco.CriarContexto();
        Assert.False(await contexto.ChavesMcp.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant])
            .AnyAsync(c => c.TenantId == inativo.Id));
    }

    [Fact]
    public async Task Revogar_chave_mcp_inativa_a_chave_e_repetir_falha()
    {
        var tenant = await TenantComCnpjValidoAsync();
        var (_, saida, _) = Executar(banco.ConnectionString, "gerar-chave-mcp", "--cnpj", tenant.Cnpj, "--descricao", "Teste");
        var chave = saida.Split('\n').Select(l => l.Trim()).Single(l => FormatoChaveMcp.TentarLerPrefixo(l, out _));
        FormatoChaveMcp.TentarLerPrefixo(chave, out var prefixo);

        var (codigo, _, _) = Executar(banco.ConnectionString, "revogar-chave-mcp", "--prefixo", prefixo);
        var (codigoRepetido, _, erroRepetido) = Executar(banco.ConnectionString, "revogar-chave-mcp", "--prefixo", prefixo);

        Assert.Equal(0, codigo);
        Assert.Equal(1, codigoRepetido);
        Assert.Contains("Nenhuma chave MCP ativa", erroRepetido);
        await using var contexto = banco.CriarContexto();
        var revogada = await contexto.ChavesMcp.IgnoreQueryFilters([PlataformaDbContext.FiltroTenant]).AsNoTracking()
            .SingleAsync(c => c.Prefixo == prefixo);
        Assert.False(revogada.Ativo);
        Assert.NotNull(revogada.RevogadoEm);
    }
}
