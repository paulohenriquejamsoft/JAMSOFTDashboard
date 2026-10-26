namespace Jamsoft.DashBoards.Api.Configuracao;

/// <summary>
/// Seção "Cors": origens do frontend que podem chamar a API pelo navegador. Só é necessária quando o
/// frontend é servido em outra origem; o Coletor (HttpClient) não passa por CORS.
/// </summary>
public sealed class CorsApiOptions
{
    public const string Secao = "Cors";

    /// <summary>Origens completas, sem barra final (ex.: <c>https://dashboards.jamsoft.com.br</c>). Vazia desliga o CORS.</summary>
    public string[] OrigensPermitidas { get; set; } = [];

    /// <summary>Sem curinga: o refresh token vai em cookie, e credenciais não combinam com <c>*</c>.</summary>
    public bool Valida() => OrigensPermitidas.All(o =>
        Uri.TryCreate(o, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http"
        && uri.GetLeftPart(UriPartial.Authority) == o);
}
