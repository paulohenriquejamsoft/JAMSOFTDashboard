using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Convencoes;

/// <summary>
/// Nomes do banco em UPPER_SNAKE_CASE (AD-012): a entidade <c>Usuario</c> vira a tabela <c>USUARIO</c>
/// e a propriedade <c>TenantId</c> vira a coluna <c>TENANT_ID</c>. Chamar no fim do OnModelCreating.
/// </summary>
public static class NomesUpperSnake
{
    public static void Aplicar(ModelBuilder modelBuilder)
    {
        foreach (var entidade in modelBuilder.Model.GetEntityTypes())
        {
            if (entidade.IsOwned()) continue;

            entidade.SetTableName(Converter(entidade.ClrType.Name));
            foreach (var propriedade in entidade.GetProperties())
                propriedade.SetColumnName(Converter(propriedade.Name));
        }
    }

    /// <summary>PascalCase → UPPER_SNAKE: <c>IdPublico</c> → <c>ID_PUBLICO</c>, <c>Linha2Endereco</c> → <c>LINHA2_ENDERECO</c>.</summary>
    public static string Converter(string nome)
    {
        var saida = new StringBuilder(nome.Length + 8);
        for (var i = 0; i < nome.Length; i++)
        {
            var c = nome[i];
            if (i > 0 && char.IsUpper(c))
            {
                var anterior = nome[i - 1];
                var proximoMinusculo = i + 1 < nome.Length && char.IsLower(nome[i + 1]);
                // Nova palavra: depois de minúscula/dígito, ou fim de sigla (IPEndereco → IP_ENDERECO).
                if (char.IsLower(anterior) || char.IsDigit(anterior) || (char.IsUpper(anterior) && proximoMinusculo))
                    saida.Append('_');
            }
            saida.Append(char.ToUpperInvariant(c));
        }
        return saida.ToString();
    }
}
