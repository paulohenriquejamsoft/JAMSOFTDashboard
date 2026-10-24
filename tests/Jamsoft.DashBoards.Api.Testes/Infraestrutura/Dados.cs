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

    /// <summary>14 dígitos aleatórios: só o esquema é testado aqui, não os dígitos verificadores.</summary>
    public static string CnpjQualquer() =>
        string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10).ToString()));

    public static async Task<SqlException> FalhaSqlAsync(Func<Task> acao)
    {
        var erro = await Assert.ThrowsAsync<DbUpdateException>(acao);
        return Assert.IsType<SqlException>(erro.InnerException);
    }
}
