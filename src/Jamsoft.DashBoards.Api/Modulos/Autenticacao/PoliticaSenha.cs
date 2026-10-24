namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

/// <summary>Política de senha da plataforma: ao menos 10 caracteres, com ao menos 1 letra e 1 dígito.</summary>
public static class PoliticaSenha
{
    public const int TamanhoMinimo = 10;

    /// <summary>Mensagens de erro; vazia quando a senha atende à política.</summary>
    public static string[] Validar(string? senha)
    {
        senha ??= "";
        var erros = new List<string>();
        if (senha.Length < TamanhoMinimo) erros.Add($"A senha precisa ter ao menos {TamanhoMinimo} caracteres.");
        if (!senha.Any(char.IsLetter)) erros.Add("A senha precisa ter ao menos uma letra.");
        if (!senha.Any(char.IsDigit)) erros.Add("A senha precisa ter ao menos um dígito.");
        return [.. erros];
    }
}
