using System.Security.Cryptography;
using System.Text;
using Jamsoft.Sge.Mcp.Configuracao;
using Microsoft.Extensions.Options;

namespace Jamsoft.Sge.Mcp.Seguranca;

/// <summary>
/// Cifra e decifra as connection strings dos clientes com AES-256-GCM.
/// Formato gravado (Base64): nonce (12 bytes) | tag (16 bytes) | texto cifrado.
/// </summary>
public sealed class ProtetorConexao
{
    private const int TamanhoNonce = 12;
    private const int TamanhoTag = 16;
    private readonly byte[] _chave;

    public ProtetorConexao(IOptions<McpOptions> opcoes) : this(opcoes.Value.ChaveCriptografia) { }

    public ProtetorConexao(string chaveBase64)
    {
        if (string.IsNullOrWhiteSpace(chaveBase64))
            throw new InvalidOperationException(
                "Mcp:ChaveCriptografia não configurada. Gere uma com 'dotnet run -- gerar-chave-mestra'.");
        _chave = Convert.FromBase64String(chaveBase64);
        if (_chave.Length != 32)
            throw new InvalidOperationException("Mcp:ChaveCriptografia precisa ter 32 bytes (AES-256) em Base64.");
    }

    public static string GerarChaveMestra() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string Cifrar(string textoClaro)
    {
        var claro = Encoding.UTF8.GetBytes(textoClaro);
        var saida = new byte[TamanhoNonce + TamanhoTag + claro.Length];
        var nonce = saida.AsSpan(0, TamanhoNonce);
        var tag = saida.AsSpan(TamanhoNonce, TamanhoTag);
        var cifrado = saida.AsSpan(TamanhoNonce + TamanhoTag);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_chave, TamanhoTag);
        aes.Encrypt(nonce, claro, cifrado, tag);
        return Convert.ToBase64String(saida);
    }

    public string Decifrar(string base64)
    {
        var dados = Convert.FromBase64String(base64);
        if (dados.Length < TamanhoNonce + TamanhoTag)
            throw new CryptographicException("Connection string cifrada inválida.");
        var nonce = dados.AsSpan(0, TamanhoNonce);
        var tag = dados.AsSpan(TamanhoNonce, TamanhoTag);
        var cifrado = dados.AsSpan(TamanhoNonce + TamanhoTag);
        var claro = new byte[cifrado.Length];
        using var aes = new AesGcm(_chave, TamanhoTag);
        aes.Decrypt(nonce, cifrado, tag, claro);
        return Encoding.UTF8.GetString(claro);
    }
}
