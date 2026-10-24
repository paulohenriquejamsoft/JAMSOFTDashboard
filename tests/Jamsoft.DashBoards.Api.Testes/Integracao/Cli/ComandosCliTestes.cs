using Jamsoft.DashBoards.Api.Cli;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
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
}
