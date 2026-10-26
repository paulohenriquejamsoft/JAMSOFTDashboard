using Jamsoft.Sge.Mcp.Seguranca;

namespace Jamsoft.Sge.Mcp.Cli;

/// <summary>
/// Comandos de linha de comando para administrar chaves sem subir o servidor:
///   dotnet run -- gerar-chave-mestra
///   dotnet run -- gerar-chave --cliente "Loja Exemplo" --conexao "Server=...;Database=...;..." [--expira 2027-12-31]
/// </summary>
public static class ComandosCli
{
    public static bool TentarExecutar(string[] args, IConfiguration configuracao)
    {
        if (args.Length == 0) return false;

        switch (args[0])
        {
            case "gerar-chave-mestra":
                Console.WriteLine("Nova Mcp:ChaveCriptografia (guarde em local seguro; trocar invalida as conexões já cifradas):");
                Console.WriteLine(ProtetorConexao.GerarChaveMestra());
                return true;

            case "gerar-chave":
                GerarChave(args, configuracao);
                return true;

            default:
                return false;
        }
    }

    private static void GerarChave(string[] args, IConfiguration configuracao)
    {
        var cliente = Argumento(args, "--cliente");
        var conexao = Argumento(args, "--conexao");
        var expira = Argumento(args, "--expira");

        if (cliente is null || conexao is null)
        {
            Console.Error.WriteLine("Uso: dotnet run -- gerar-chave --cliente \"Nome\" --conexao \"connection string\" [--expira yyyy-MM-dd]");
            Environment.ExitCode = 1;
            return;
        }

        var protetor = new ProtetorConexao(configuracao["Mcp:ChaveCriptografia"] ?? "");
        var chave = ApiKey.Gerar();
        ApiKey.TentarLerPrefixo(chave, out var prefixo);
        var hashHex = Convert.ToHexString(ApiKey.Hash(chave));
        var cifrada = protetor.Cifrar(conexao);
        var expiraSql = expira is null ? "NULL" : $"'{DateOnly.Parse(expira):yyyy-MM-dd}'";

        Console.WriteLine();
        Console.WriteLine("api-key (mostrada só agora; entregue ao cliente por canal seguro):");
        Console.WriteLine($"  {chave}");
        Console.WriteLine();
        Console.WriteLine("Rode no banco central:");
        Console.WriteLine($"""
            INSERT INTO dbo.McpApiKey (Prefixo, HashChave, Cliente, ConnectionStringCifrada, Ativo, ExpiraEm)
            VALUES ('{prefixo}', 0x{hashHex}, N'{cliente.Replace("'", "''")}', '{cifrada}', 1, {expiraSql});
            """);
    }

    private static string? Argumento(string[] args, string nome)
    {
        var i = Array.IndexOf(args, nome);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
