namespace Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;

public enum TipoAutenticacao
{
    Windows,
    Sql,
}

/// <summary>
/// Instância de SQL Server do ERP (CFG-03). <see cref="Senha"/> fica em texto só na memória; no
/// <c>config.json</c> vai cifrada com DPAPI (CFG-08).
/// </summary>
public sealed record InstanciaConexao(
    Guid Id,
    string Servidor,
    int? Porta,
    TipoAutenticacao Autenticacao,
    string? Usuario,
    string? Senha);

/// <summary>Credencial do Coletor na API (CFG-02). O segredo vai cifrado para o arquivo (CFG-08).</summary>
public sealed record CredencialApi(string UrlApi, Guid ClientId, string ClientSecret);
