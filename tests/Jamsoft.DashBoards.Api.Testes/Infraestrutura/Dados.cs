using System.Security.Cryptography;
using Jamsoft.DashBoards.Api.Modulos.Coletores;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Jamsoft.DashBoards.Api.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Geradores de valores únicos e asserções comuns aos testes de esquema.</summary>
public static class Dados
{
    /// <summary>Erro do SQL Server para chave duplicada em índice único.</summary>
    public const int ErroIndiceUnico = 2601;
    /// <summary>Erro do SQL Server para violação de CHECK constraint.</summary>
    public const int ErroCheck = 547;
    /// <summary>Erro do SQL Server para chave primária duplicada.</summary>
    public const int ErroChavePrimaria = 2627;
    /// <summary>Erro do SQL Server para valor maior que a coluna (truncamento).</summary>
    public const int ErroTruncamento = 2628;

    /// <summary>14 dígitos aleatórios: só o esquema é testado aqui, não os dígitos verificadores.</summary>
    public static string CnpjQualquer() =>
        string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10).ToString()));

    /// <summary>CNPJ aleatório com dígitos verificadores corretos (fictício), para as rotas que validam o CNPJ.</summary>
    public static string CnpjValido()
    {
        var digitos = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        digitos[0] = Random.Shared.Next(1, 10);
        digitos[11] = 0; // base não repetida: o primeiro dígito nunca é 0, o último sempre é
        digitos.Add(Verificador(digitos, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digitos.Add(Verificador(digitos, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digitos);
    }

    private static int Verificador(List<int> digitos, int[] pesos)
    {
        var resto = pesos.Select((peso, i) => peso * digitos[i]).Sum() % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    /// <summary>Grava um tenant ativo com CNPJ único e devolve o ID interno.</summary>
    public static async Task<int> CriarTenantAsync(BancoDeTestes banco, string nome = "Tenant de teste")
    {
        await using var contexto = banco.CriarContexto();
        var tenant = new Tenant { Nome = nome, Cnpj = CnpjQualquer() };
        contexto.Tenants.Add(tenant);
        await contexto.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>Grava um tenant com CNPJ único e devolve a entidade (com ID e ID_PUBLICO).</summary>
    public static async Task<Tenant> NovoTenantAsync(BancoDeTestes banco, string nome = "Tenant de teste", bool ativo = true)
    {
        await using var contexto = banco.CriarContexto();
        var tenant = new Tenant { Nome = nome, Cnpj = CnpjQualquer(), Ativo = ativo };
        contexto.Tenants.Add(tenant);
        await contexto.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Senha usada por <see cref="NovoUsuarioAsync"/> quando <c>comSenha</c> é verdadeiro.</summary>
    public const string Senha = "SenhaDeTeste123";

    /// <summary>Grava um usuário com e-mail único (SuperAdmin sem tenant) e devolve a entidade.
    /// Com <c>comSenha</c>, a senha é <see cref="Senha"/>.</summary>
    public static async Task<Usuario> NovoUsuarioAsync(BancoDeTestes banco, Perfil perfil, Tenant? tenant = null,
        bool trocarSenha = false, bool ativo = true, bool comSenha = false)
    {
        await using var contexto = banco.CriarContexto();
        var usuario = new Usuario
        {
            TenantId = tenant?.Id,
            Nome = $"Usuário {perfil}",
            Email = $"{Guid.NewGuid():N}@teste.com",
            SenhaHash = "hash-de-teste",
            Perfil = perfil,
            TrocarSenha = trocarSenha,
            Ativo = ativo,
        };
        // PBKDF2 é lento: só calcula o hash real quando o teste vai fazer login.
        if (comSenha) usuario.SenhaHash = new PasswordHasher<Usuario>().HashPassword(usuario, Senha);
        contexto.Usuarios.Add(usuario);
        await contexto.SaveChangesAsync();
        return usuario;
    }

    /// <summary>Grava um coletor ativo com <c>CLIENT_ID</c> único e hash qualquer e devolve a entidade.</summary>
    public static async Task<Coletor> NovoColetorAsync(BancoDeTestes banco, string nome = "Coletor de teste")
    {
        await using var contexto = banco.CriarContexto();
        var coletor = new Coletor
        {
            Nome = nome,
            ClientId = Guid.NewGuid(),
            SecretHash = SHA256.HashData(Guid.NewGuid().ToByteArray()),
        };
        contexto.Coletores.Add(coletor);
        await contexto.SaveChangesAsync();
        return coletor;
    }

    public static async Task<SqlException> FalhaSqlAsync(Func<Task> acao)
    {
        var erro = await Assert.ThrowsAsync<DbUpdateException>(acao);
        return Assert.IsType<SqlException>(erro.InnerException);
    }
}
