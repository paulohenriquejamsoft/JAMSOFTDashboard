using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;

namespace Jamsoft.DashBoards.Coletor;

/// <summary>
/// Instâncias de SQL Server do ERP (CFG-03): servidor, porta, autenticação Windows ou SQL, usuário e senha, com
/// "Testar conexão" e a lista de bancos. Falhas mostram o tipo (rede, autenticação, timeout) e nunca a senha (CFG-04);
/// a senha gravada nunca volta para a tela (CFG-08).
/// </summary>
internal sealed class FormInstancia : Form
{
    private const string TextoWindows = "Windows";
    private const string TextoSql = "SQL Server";

    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly ConexoesErp _conexoes;
    private readonly ListBox _lista = new() { Dock = DockStyle.Left, Width = 230, IntegralHeight = false };
    private readonly TextBox _servidor = new() { PlaceholderText = @"SERVIDOR\INSTANCIA" };
    private readonly TextBox _porta = new() { PlaceholderText = "(padrão)" };
    private readonly ComboBox _autenticacao = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _usuario = new();
    private readonly TextBox _senha = Tela.Segredo();
    private readonly Label _mensagem = Tela.Mensagem();
    private readonly ListBox _bancos = new() { Height = 110, IntegralHeight = false };
    private readonly Button _testar;
    private readonly Button _salvar;
    private InstanciaConexao? _editando;
    private Guid _id = Guid.NewGuid();

    public FormInstancia(ArmazenamentoConfiguracao armazenamento, ConexoesErp conexoes)
    {
        _armazenamento = armazenamento;
        _conexoes = conexoes;
        Tela.Dialogo(this, "Instâncias de conexão", 760, 430);

        _autenticacao.Items.AddRange([TextoWindows, TextoSql]);
        _autenticacao.SelectedIndex = 0;
        _autenticacao.SelectedIndexChanged += (_, _) => AjustarAutenticacao();
        _lista.SelectedIndexChanged += (_, _) =>
        {
            if (_lista.SelectedItem is ItemInstancia item)
            {
                Preencher(item.Instancia);
            }
        };

        var grade = Tela.Grade();
        Tela.Linha(grade, "Servidor", _servidor);
        Tela.Linha(grade, "Porta", _porta);
        Tela.Linha(grade, "Autenticação", _autenticacao);
        Tela.Linha(grade, "Usuário", _usuario);
        Tela.Linha(grade, "Senha", _senha);
        Tela.Linha(grade, _mensagem);
        Tela.Linha(grade, "Bancos", _bancos);

        _testar = Tela.Botao("Testar conexão", async (_, _) => await TestarAsync());
        _salvar = Tela.Botao("Salvar", async (_, _) => await SalvarAsync());
        var fechar = Tela.Botao("Fechar", (_, _) => Close());
        CancelButton = fechar;
        Controls.Add(grade);
        Controls.Add(_lista);
        Controls.Add(Tela.Rodape(fechar, _salvar, _testar, Tela.Botao("Nova", (_, _) => Limpar())));

        Carregar();
        Limpar();
    }

    private void Carregar(Guid? selecionar = null)
    {
        _lista.Items.Clear();
        foreach (var instancia in _armazenamento.Carregar().Instancias)
        {
            var indice = _lista.Items.Add(new ItemInstancia(instancia));
            if (instancia.Id == selecionar)
            {
                _lista.SelectedIndex = indice;
            }
        }
    }

    private void Limpar()
    {
        _lista.ClearSelected();
        _editando = null;
        _id = Guid.NewGuid();
        _servidor.Clear();
        _porta.Clear();
        _autenticacao.SelectedIndex = 0;
        _usuario.Clear();
        _senha.Clear();
        _senha.PlaceholderText = "";
        _bancos.Items.Clear();
        _mensagem.Text = "";
        AjustarAutenticacao();
        _servidor.Focus();
    }

    private void Preencher(InstanciaConexao instancia)
    {
        _editando = instancia;
        _id = instancia.Id;
        _servidor.Text = instancia.Servidor;
        _porta.Text = instancia.Porta?.ToString() ?? "";
        _autenticacao.SelectedItem = instancia.Autenticacao == TipoAutenticacao.Sql ? TextoSql : TextoWindows;
        _usuario.Text = instancia.Usuario ?? "";
        _senha.Clear();
        _senha.PlaceholderText = instancia.Senha is null ? "" : "(gravada; deixe em branco para manter)";
        _bancos.Items.Clear();
        _mensagem.Text = "";
        AjustarAutenticacao();
    }

    private void AjustarAutenticacao() => _usuario.Enabled = _senha.Enabled = _autenticacao.SelectedItem is TextoSql;

    /// <summary>Instância digitada; senha em branco mantém a gravada.</summary>
    private InstanciaConexao? Montar()
    {
        if (string.IsNullOrWhiteSpace(_servidor.Text))
        {
            return Erro("Informe o servidor.", _servidor);
        }

        int? porta = null;
        if (!string.IsNullOrWhiteSpace(_porta.Text))
        {
            if (!int.TryParse(_porta.Text.Trim(), out var numero) || numero is < 1 or > 65535)
            {
                return Erro("Porta inválida: use um número de 1 a 65535 ou deixe em branco.", _porta);
            }

            porta = numero;
        }

        if (_autenticacao.SelectedItem is not TextoSql)
        {
            return new InstanciaConexao(_id, _servidor.Text.Trim(), porta, TipoAutenticacao.Windows, null, null);
        }

        if (string.IsNullOrWhiteSpace(_usuario.Text))
        {
            return Erro("Informe o usuário do SQL Server.", _usuario);
        }

        var senha = _senha.Text.Length > 0 ? _senha.Text : _editando?.Senha;
        return new InstanciaConexao(_id, _servidor.Text.Trim(), porta, TipoAutenticacao.Sql, _usuario.Text.Trim(), senha);
    }

    /// <summary>CFG-03/04: lista os bancos ou mostra o tipo da falha.</summary>
    private async Task<bool> TestarAsync()
    {
        if (Montar() is not { } instancia)
        {
            return false;
        }

        Ocupado(true);
        _bancos.Items.Clear();
        _mensagem.ForeColor = SystemColors.ControlText;
        _mensagem.Text = "Testando a conexão...";
        try
        {
            var resultado = await _conexoes.TestarAsync(instancia);
            if (resultado.Sucesso)
            {
                _bancos.Items.AddRange([.. resultado.Bancos]);
                _mensagem.ForeColor = Color.DarkGreen;
                _mensagem.Text = $"Conexão bem-sucedida: {resultado.Bancos.Count} banco(s) disponível(is).";
                return true;
            }

            _mensagem.ForeColor = Color.Firebrick;
            _mensagem.Text = $"Tipo de erro: {NomeFalha(resultado.Falha!.Value)}{Environment.NewLine}{resultado.Mensagem}";
            return false;
        }
        finally
        {
            Ocupado(false);
        }
    }

    private async Task SalvarAsync()
    {
        if (Montar() is not { } instancia)
        {
            return;
        }

        if (!await TestarAsync() && !Tela.Confirmar(this, "A conexão falhou. Salvar a instância mesmo assim?"))
        {
            return;
        }

        _armazenamento.SalvarInstancia(instancia);
        var bancos = _bancos.Items.Cast<object>().ToArray();
        var mensagem = _mensagem.Text;
        Carregar(instancia.Id);
        _bancos.Items.AddRange(bancos);
        _mensagem.Text = $"Instância salva. {mensagem}";
    }

    private InstanciaConexao? Erro(string mensagem, Control campo)
    {
        _mensagem.ForeColor = Color.Firebrick;
        _mensagem.Text = mensagem;
        campo.Focus();
        return null;
    }

    private void Ocupado(bool ocupado)
    {
        _testar.Enabled = _salvar.Enabled = _lista.Enabled = !ocupado;
        UseWaitCursor = ocupado;
    }

    private static string NomeFalha(FalhaConexao falha) => falha switch
    {
        FalhaConexao.Autenticacao => "autenticação",
        FalhaConexao.Timeout => "timeout",
        _ => "rede",
    };

    private sealed record ItemInstancia(InstanciaConexao Instancia)
    {
        public override string ToString() => Descrever(Instancia);
    }

    /// <summary>Servidor[,porta] e o tipo de autenticação, sem senha.</summary>
    public static string Descrever(InstanciaConexao instancia)
    {
        var servidor = instancia.Porta is { } porta ? $"{instancia.Servidor},{porta}" : instancia.Servidor;
        return instancia.Autenticacao == TipoAutenticacao.Sql ? $"{servidor} (SQL: {instancia.Usuario})" : $"{servidor} (Windows)";
    }
}
