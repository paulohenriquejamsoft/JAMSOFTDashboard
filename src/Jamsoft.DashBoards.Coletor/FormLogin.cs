using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

namespace Jamsoft.DashBoards.Coletor;

/// <summary>
/// Login na API (CFG-02): URL, client id e client secret. No sucesso grava a credencial cifrada (CFG-08), passa a
/// usá-la no agendador e reinicia as configurações ativas. O secret gravado nunca volta para a tela.
/// </summary>
internal sealed class FormLogin : Form
{
    private readonly SessaoApi _sessao;
    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly ComandosConfiguracao _comandos;
    private readonly CredencialApi? _gravada;
    private readonly TextBox _url = new();
    private readonly TextBox _clientId = new();
    private readonly TextBox _clientSecret = Tela.Segredo();
    private readonly Label _mensagem = Tela.Mensagem();
    private readonly Button _entrar;
    private CancellationTokenSource? _autenticando;

    public FormLogin(SessaoApi sessao, ArmazenamentoConfiguracao armazenamento, ComandosConfiguracao comandos)
    {
        _sessao = sessao;
        _armazenamento = armazenamento;
        _comandos = comandos;
        _gravada = armazenamento.Carregar().Credencial;

        Tela.Dialogo(this, "Login na API", 520, 250);
        _url.PlaceholderText = "https://dashboards.exemplo.com.br";
        _url.Text = _gravada?.UrlApi ?? "";
        _clientId.Text = _gravada?.ClientId.ToString() ?? "";
        if (_gravada is not null)
        {
            _clientSecret.PlaceholderText = "(gravado; deixe em branco para manter)";
        }

        var grade = Tela.Grade();
        Tela.Linha(grade, "URL da API", _url);
        Tela.Linha(grade, "Client ID", _clientId);
        Tela.Linha(grade, "Client secret", _clientSecret);
        Tela.Linha(grade, _mensagem);

        _entrar = Tela.Botao("Entrar", async (_, _) => await EntrarAsync());
        var cancelar = Tela.Botao("Cancelar", (_, _) => Close());
        AcceptButton = _entrar;
        CancelButton = cancelar;
        Controls.Add(grade);
        Controls.Add(Tela.Rodape(cancelar, _entrar));
        FormClosing += (_, _) => _autenticando?.Cancel();
    }

    private async Task EntrarAsync()
    {
        if (Ler() is not { } credencial)
        {
            return;
        }

        Habilitar(false);
        _mensagem.ForeColor = SystemColors.ControlText;
        _mensagem.Text = "Autenticando... com a API instável, pode levar até 2 minutos.";
        using var autenticando = _autenticando = new CancellationTokenSource();
        try
        {
            var resposta = await _sessao.EntrarAsync(credencial, autenticando.Token);
            if (resposta.Sucesso)
            {
                _armazenamento.SalvarCredencial(credencial);
                _comandos.Recarregar();
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            Erro(resposta.Situacao switch
            {
                SituacaoResposta.CredencialInvalida => "Credencial inválida ou revogada.",
                SituacaoResposta.FalhaComunicacao => $"Não foi possível falar com a API. {resposta.Mensagem}",
                _ => resposta.Mensagem ?? "A API recusou o login.",
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _autenticando = null;
        }

        _clientSecret.Clear();
        Habilitar(true);
    }

    /// <summary>Credencial digitada; secret em branco reaproveita o gravado do mesmo client id.</summary>
    private CredencialApi? Ler()
    {
        if (!Uri.TryCreate(_url.Text.Trim(), UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            return Erro("Informe a URL da API (http ou https).", _url);
        }

        if (!Guid.TryParse(_clientId.Text.Trim(), out var clientId))
        {
            return Erro("Client ID inválido: deve ser um GUID.", _clientId);
        }

        var segredo = _clientSecret.Text.Trim();
        if (segredo.Length == 0 && _gravada?.ClientId == clientId)
        {
            segredo = _gravada.ClientSecret;
        }

        return segredo.Length == 0
            ? Erro("Informe o client secret.", _clientSecret)
            : new CredencialApi(url.ToString(), clientId, segredo);
    }

    private CredencialApi? Erro(string mensagem, Control? campo = null)
    {
        _mensagem.ForeColor = Color.Firebrick;
        _mensagem.Text = mensagem;
        campo?.Focus();
        return null;
    }

    private void Habilitar(bool habilitado)
    {
        _url.Enabled = _clientId.Enabled = _clientSecret.Enabled = _entrar.Enabled = habilitado;
    }
}
