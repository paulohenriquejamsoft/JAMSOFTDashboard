using System.Security.Cryptography;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Cli;

/// <summary>
/// Comandos de implantação que rodam e saem sem subir o servidor:
///   dotnet run -- migrar
///   dotnet run -- criar-superadmin --email admin@jamsoft.com.br --nome "Nome"
/// Usam a connection string "Plataforma" da configuração.
/// </summary>
public static class ComandosCli
{
    /// <summary>Sem caracteres ambíguos (0/O, 1/l/I).</summary>
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int TamanhoSenhaTemporaria = 16;

    /// <summary>Executa o comando de <paramref name="args"/>; false quando não é um comando (sobe o servidor).</summary>
    public static bool TentarExecutar(string[] args, IConfiguration configuracao)
    {
        var codigo = Executar(args, configuracao, Console.Out, Console.Error);
        if (codigo is null) return false;
        Environment.ExitCode = codigo.Value;
        return true;
    }

    /// <summary>Código de saída do comando (0 = sucesso), ou nulo quando <paramref name="args"/> não é um comando.</summary>
    public static int? Executar(string[] args, IConfiguration configuracao, TextWriter saida, TextWriter erro)
    {
        if (args.Length == 0) return null;

        return args[0] switch
        {
            "migrar" => MigrarAsync(configuracao, saida).GetAwaiter().GetResult(),
            "criar-superadmin" => CriarSuperAdminAsync(args, configuracao, saida, erro).GetAwaiter().GetResult(),
            _ => null,
        };
    }

    private static async Task<int> MigrarAsync(IConfiguration configuracao, TextWriter saida)
    {
        await using var banco = Contexto(configuracao);
        await banco.Database.MigrateAsync();
        saida.WriteLine("Migrações aplicadas.");
        return 0;
    }

    private static async Task<int> CriarSuperAdminAsync(string[] args, IConfiguration configuracao, TextWriter saida,
        TextWriter erro)
    {
        var email = Argumento(args, "--email");
        var nome = Argumento(args, "--nome");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(nome))
        {
            erro.WriteLine("Uso: dotnet run -- criar-superadmin --email email@dominio --nome \"Nome\"");
            return 1;
        }

        email = Usuario.NormalizarEmail(email);
        await using var banco = Contexto(configuracao);
        if (await banco.Usuarios.AnyAsync(u => u.Email == email))
        {
            erro.WriteLine($"Já existe um usuário com o e-mail {email}. Nada foi criado.");
            return 1;
        }

        var senha = GerarSenhaTemporaria();
        var usuario = new Usuario
        {
            Nome = nome.Trim(),
            Email = email,
            Perfil = Perfil.SuperAdmin,
            TenantId = null,
            Ativo = true,
            TrocarSenha = true,
        };
        usuario.SenhaHash = new PasswordHasher<Usuario>().HashPassword(usuario, senha);
        banco.Usuarios.Add(usuario);
        await banco.SaveChangesAsync();

        saida.WriteLine($"SuperAdmin {email} criado. Troca de senha obrigatória no primeiro login.");
        saida.WriteLine("Senha temporária (mostrada só agora; entregue por canal seguro):");
        saida.WriteLine($"  {senha}");
        return 0;
    }

    /// <summary>16 caracteres aleatórios com ao menos uma letra e um dígito (política de senha).</summary>
    private static string GerarSenhaTemporaria()
    {
        while (true)
        {
            var senha = RandomNumberGenerator.GetString(Alfabeto, TamanhoSenhaTemporaria);
            if (senha.Any(char.IsLetter) && senha.Any(char.IsDigit)) return senha;
        }
    }

    private static PlataformaDbContext Contexto(IConfiguration configuracao)
    {
        var options = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(options, configuracao.GetConnectionString("Plataforma"));
        return new PlataformaDbContext(options.Options, TenantSemAcesso.Instancia);
    }

    private static string? Argumento(string[] args, string nome)
    {
        var i = Array.IndexOf(args, nome);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
