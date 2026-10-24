namespace Jamsoft.DashBoards.Api.Modulos.Tenants;

/// <summary>CNPJ do tenant (TEN-01): gravado só com os 14 dígitos.</summary>
public static class Cnpj
{
    public const int Tamanho = 14;

    private static readonly int[] PesosPrimeiro = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] PesosSegundo = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>
    /// Remove a máscara (<c>.</c>, <c>/</c>, <c>-</c> e espaços) e confere 14 dígitos e os dígitos
    /// verificadores. Recusa letras e sequências de um só dígito repetido.
    /// </summary>
    public static bool TentarNormalizar(string? valor, out string normalizado)
    {
        normalizado = "";
        if (valor is null) return false;

        var digitos = new List<int>(Tamanho);
        foreach (var c in valor)
        {
            if (char.IsAsciiDigit(c)) digitos.Add(c - '0');
            else if (c is not ('.' or '/' or '-' or ' ')) return false;
        }

        if (digitos.Count != Tamanho || digitos.All(d => d == digitos[0])) return false;
        if (digitos[12] != Verificador(digitos, PesosPrimeiro) || digitos[13] != Verificador(digitos, PesosSegundo))
            return false;

        normalizado = string.Concat(digitos);
        return true;
    }

    /// <summary>Dígito verificador pelos <paramref name="pesos"/> sobre os primeiros dígitos.</summary>
    private static int Verificador(List<int> digitos, int[] pesos)
    {
        var resto = pesos.Select((peso, i) => peso * digitos[i]).Sum() % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
