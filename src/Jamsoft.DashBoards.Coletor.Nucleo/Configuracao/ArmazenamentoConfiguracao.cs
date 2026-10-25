using System.Text.Json;
using System.Text.Json.Serialization;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;

/// <summary>
/// <c>config.json</c> na pasta de dados (AD-016): URL e credencial da API, instâncias e configurações. Senhas e
/// <c>client_secret</c> vão cifrados com <see cref="ProtetorSegredos"/> (CFG-08); toda gravação é atômica.
/// Uma instância por pasta (singleton): o lock serializa leitura-alteração-gravação.
/// </summary>
public sealed class ArmazenamentoConfiguracao(string pastaDados, Checkpoints checkpoints, TimeProvider relogio)
{
    public const string NomeArquivo = "config.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _caminho = Path.Combine(pastaDados, NomeArquivo);
    private readonly Lock _lock = new();

    public DadosColetor Carregar()
    {
        lock (_lock)
        {
            var arquivo = Ler();
            return new DadosColetor(
                arquivo.Credencial is { } c ? new CredencialApi(c.UrlApi, c.ClientId, ProtetorSegredos.Decifrar(c.ClientSecretCifrado)) : null,
                [.. arquivo.Instancias.Select(i => new InstanciaConexao(i.Id, i.Servidor, i.Porta, i.Autenticacao, i.Usuario,
                    i.SenhaCifrada is null ? null : ProtetorSegredos.Decifrar(i.SenhaCifrada)))],
                arquivo.Configuracoes);
        }
    }

    /// <summary>CFG-07: 6 meses antes de hoje, no momento da carga inicial.</summary>
    public DateOnly PeriodoInicialPadrao() => DateOnly.FromDateTime(relogio.GetLocalNow().DateTime).AddMonths(-6);

    public void SalvarCredencial(CredencialApi credencial) =>
        Alterar(arquivo => arquivo with
        {
            Credencial = new CredencialArquivo(credencial.UrlApi, credencial.ClientId, ProtetorSegredos.Cifrar(credencial.ClientSecret)),
        });

    /// <summary>Inclui ou substitui (pelo <see cref="InstanciaConexao.Id"/>).</summary>
    public void SalvarInstancia(InstanciaConexao instancia) =>
        Alterar(arquivo => arquivo with
        {
            Instancias = Substituir(arquivo.Instancias, i => i.Id == instancia.Id, new InstanciaArquivo(
                instancia.Id, instancia.Servidor, instancia.Porta, instancia.Autenticacao, instancia.Usuario,
                instancia.Senha is null ? null : ProtetorSegredos.Cifrar(instancia.Senha))),
        });

    /// <summary>Valida (CFG-05, CFG-06) e inclui ou substitui (pelo <see cref="ConfiguracaoSincronizacao.Id"/>).</summary>
    /// <exception cref="ConfiguracaoInvalidaException">Campo obrigatório ausente, fora dos limites ou banco já vinculado.</exception>
    public void SalvarConfiguracao(ConfiguracaoSincronizacao configuracao) =>
        Alterar(arquivo =>
        {
            Validar(arquivo, configuracao);
            return arquivo with { Configuracoes = Substituir(arquivo.Configuracoes, c => c.Id == configuracao.Id, configuracao) };
        });

    /// <summary>Remove a configuração e o seu checkpoint, sem nada na API (CFG-09).</summary>
    public void ExcluirConfiguracao(Guid id)
    {
        lock (_lock)
        {
            var arquivo = Ler();
            if (arquivo.Configuracoes.FirstOrDefault(c => c.Id == id) is not { } excluida)
            {
                return;
            }

            Gravar(arquivo with { Configuracoes = [.. arquivo.Configuracoes.Where(c => c.Id != id)] });
            checkpoints.Zerar(excluida.Id, excluida.Rotina);
        }
    }

    private static void Validar(Arquivo arquivo, ConfiguracaoSincronizacao configuracao)
    {
        if (configuracao.Tenant == Guid.Empty)
        {
            throw new ConfiguracaoInvalidaException(nameof(configuracao.Tenant), "Informe o tenant.");
        }

        if (arquivo.Instancias.All(i => i.Id != configuracao.InstanciaId))
        {
            throw new ConfiguracaoInvalidaException(nameof(configuracao.InstanciaId), "Informe uma instância cadastrada.");
        }

        if (string.IsNullOrWhiteSpace(configuracao.Banco))
        {
            throw new ConfiguracaoInvalidaException(nameof(configuracao.Banco), "Informe o banco de origem.");
        }

        if (configuracao.Rotina != ConfiguracaoSincronizacao.RotinaVendas)
        {
            throw new ConfiguracaoInvalidaException(nameof(configuracao.Rotina), $"Rotina deve ser \"{ConfiguracaoSincronizacao.RotinaVendas}\".");
        }

        if (configuracao.Intervalo < ConfiguracaoSincronizacao.IntervaloMinimo || configuracao.Intervalo > ConfiguracaoSincronizacao.IntervaloMaximo)
        {
            throw new ConfiguracaoInvalidaException(nameof(configuracao.Intervalo), "O intervalo deve ficar entre 5 minutos e 24 horas.");
        }

        if (configuracao.JanelaReprocessamentoDias is < 0 or > ConfiguracaoSincronizacao.JanelaMaximaDias)
        {
            throw new ConfiguracaoInvalidaException(nameof(configuracao.JanelaReprocessamentoDias), "A janela de reprocessamento deve ficar entre 0 e 90 dias.");
        }

        var outra = arquivo.Configuracoes.FirstOrDefault(c =>
            c.Id != configuracao.Id
            && c.Tenant != configuracao.Tenant
            && c.Rotina == configuracao.Rotina
            && c.InstanciaId == configuracao.InstanciaId
            && string.Equals(c.Banco, configuracao.Banco, StringComparison.OrdinalIgnoreCase));
        if (outra is not null)
        {
            throw new ConfiguracaoInvalidaException(nameof(configuracao.Banco), $"Banco já vinculado ao tenant {outra.NomeTenant}");
        }
    }

    private static List<T> Substituir<T>(IEnumerable<T> itens, Func<T, bool> mesmo, T novo)
    {
        var lista = itens.ToList();
        var indice = lista.FindIndex(i => mesmo(i));
        if (indice < 0)
        {
            lista.Add(novo);
        }
        else
        {
            lista[indice] = novo;
        }

        return lista;
    }

    private void Alterar(Func<Arquivo, Arquivo> alteracao)
    {
        lock (_lock)
        {
            Gravar(alteracao(Ler()));
        }
    }

    private Arquivo Ler()
    {
        if (!File.Exists(_caminho))
        {
            return new Arquivo(null, [], []);
        }

        using var arquivo = File.OpenRead(_caminho);
        return JsonSerializer.Deserialize<Arquivo>(arquivo, Json) ?? new Arquivo(null, [], []);
    }

    private void Gravar(Arquivo arquivo) => ArquivoAtomico.Gravar(_caminho, destino => JsonSerializer.Serialize(destino, arquivo, Json));

    private sealed record Arquivo(
        CredencialArquivo? Credencial,
        IReadOnlyList<InstanciaArquivo> Instancias,
        IReadOnlyList<ConfiguracaoSincronizacao> Configuracoes);

    private sealed record CredencialArquivo(string UrlApi, Guid ClientId, string ClientSecretCifrado);

    private sealed record InstanciaArquivo(
        Guid Id,
        string Servidor,
        int? Porta,
        TipoAutenticacao Autenticacao,
        string? Usuario,
        string? SenhaCifrada);
}
