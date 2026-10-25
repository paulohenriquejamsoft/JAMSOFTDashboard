using Jamsoft.DashBoards.Coletor.Nucleo.Api;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

namespace Jamsoft.DashBoards.Coletor;

/// <summary>
/// Tela principal (CFG-10): uma linha por configuração com tenant, banco, situação, intervalo, último sucesso e último
/// resultado, atualizada pelos eventos do <see cref="Agendador"/>. Ações editar, excluir (CFG-09), pausar (CTL-01),
/// retomar (CTL-02) e reiniciar (CTL-03). Sem login, só login e logs ficam disponíveis (CFG-01).
/// </summary>
internal sealed class FormPrincipal : Form
{
    private readonly SessaoApi _sessao;
    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly RegistroExecucoes _registro;
    private readonly Agendador _agendador;
    private readonly ComandosConfiguracao _comandos;
    private readonly ConexoesErp _conexoes;
    private readonly LeitorLogs _leitor;
    private readonly SynchronizationContext _ui;
    private readonly DataGridView _grade = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _nova;
    private readonly Button _editar;
    private readonly Button _excluir;
    private readonly Button _pausar;
    private readonly Button _retomar;
    private readonly Button _reiniciar;
    private readonly Button _instancias;
    private readonly System.Windows.Forms.Timer _relogioTela = new() { Interval = 2000 };
    private IReadOnlyList<ConfiguracaoSincronizacao> _configuracoes = [];
    private bool _loginAberto;

    public FormPrincipal(SessaoApi sessao, ArmazenamentoConfiguracao armazenamento, RegistroExecucoes registro, Agendador agendador,
        ComandosConfiguracao comandos, ConexoesErp conexoes, LeitorLogs leitor)
    {
        _sessao = sessao;
        _armazenamento = armazenamento;
        _registro = registro;
        _agendador = agendador;
        _comandos = comandos;
        _conexoes = conexoes;
        _leitor = leitor;
        // Criado na thread da tela: os eventos do agendador (outra thread) voltam por aqui.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Tela.Titulo;
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 560);

        Coluna("Tenant", 18);
        Coluna("Banco", 16);
        Coluna("Situação", 12);
        Coluna("Intervalo", 8);
        Coluna("Último sucesso", 12);
        Coluna("Último resultado", 24);
        Coluna("Próxima execução", 10);

        _nova = Tela.Botao("Nova configuração", (_, _) => Editar(nova: true));
        _editar = Tela.Botao("Editar", (_, _) => Editar());
        _excluir = Tela.Botao("Excluir", (_, _) => Excluir());
        _pausar = Tela.Botao("Pausar", (_, _) => Comandar(c => _comandos.Pausar(c.Id)));
        _retomar = Tela.Botao("Retomar", (_, _) => Comandar(c => _comandos.Retomar(c.Id)));
        _reiniciar = Tela.Botao("Reiniciar", (_, _) => Reiniciar());
        _instancias = Tela.Botao("Instâncias", (_, _) => Abrir(new FormInstancia(_armazenamento, _conexoes)));
        var logs = Tela.Botao("Logs", (_, _) => Abrir(new FormLogs(_leitor, _armazenamento)));
        var login = Tela.Botao("Login", (_, _) => PedirLogin());
        _grade.SelectionChanged += (_, _) => AjustarBotoes();
        _grade.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _editar.Enabled)
            {
                Editar();
            }
        };

        var acoes = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 0) };
        acoes.Controls.AddRange([_nova, _editar, _excluir, _pausar, _retomar, _reiniciar, _instancias, logs, login]);
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(9, 0, 9, 0) };
        rodape.Controls.Add(_status);

        Controls.Add(_grade);
        Controls.Add(acoes);
        Controls.Add(rodape);

        _agendador.EstadoAlterado += (_, _) => _ui.Post(_ => Atualizar(), null);
        _agendador.CredencialRecusada += (_, _) => _ui.Post(_ =>
        {
            _sessao.Encerrar();
            PedirLogin();
        }, null);
        _sessao.Alterada += (_, _) => Atualizar();
        // Pausar e retomar gravam a situação na thread do agendador, às vezes sem evento: a tela relê de tempos em tempos.
        _relogioTela.Tick += (_, _) => Atualizar();
        _relogioTela.Start();
        FormClosed += (_, _) => _relogioTela.Dispose();

        Atualizar();
        _ui.Post(async _ => await EntrarComGravadaAsync(), null);
    }

    /// <summary>Com a credencial gravada, autentica sozinho ao abrir o Coletor; recusada, pede login.</summary>
    private async Task EntrarComGravadaAsync()
    {
        if (_armazenamento.Carregar().Credencial is not { } credencial)
        {
            return;
        }

        _status.Text = "Autenticando na API...";
        var resposta = await _sessao.EntrarAsync(credencial);
        if (resposta.Situacao == SituacaoResposta.CredencialInvalida)
        {
            PedirLogin();
        }
        else if (!resposta.Sucesso)
        {
            Atualizar();
            _status.Text = $"Sem login: {resposta.Mensagem} Use \"Login\" para tentar de novo. Só login e logs ficam disponíveis.";
        }
    }

    private void Atualizar()
    {
        var selecionada = Selecionada()?.Id;
        _configuracoes = _armazenamento.Carregar().Configuracoes;
        _grade.Rows.Clear();
        foreach (var configuracao in _configuracoes)
        {
            var execucao = _registro.Obter(configuracao.Id);
            var agendamento = _agendador.Estado(configuracao.Id);
            var linha = _grade.Rows.Add(
                configuracao.NomeTenant,
                configuracao.Banco,
                Situacao(configuracao, execucao, agendamento),
                Intervalo(configuracao.Intervalo),
                execucao?.UltimoSucessoEm?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "—",
                Resultado(execucao),
                agendamento?.ProximaExecucao?.ToLocalTime().ToString("dd/MM HH:mm") ?? "");
            _grade.Rows[linha].Tag = configuracao.Id;
            _grade.Rows[linha].Cells[5].ToolTipText = execucao?.UltimaMensagem ?? "";
            if (configuracao.Id == selecionada)
            {
                _grade.Rows[linha].Selected = true;
            }
        }

        if (selecionada is null)
        {
            _grade.ClearSelection();
        }

        _status.Text = _sessao.Autenticada
            ? $"Conectado à API: {_sessao.Tenants.Count} tenant(s) autorizado(s). Dados em {Program.PastaDados}"
            : "Sem login na API: só login e logs ficam disponíveis.";
        AjustarBotoes();
    }

    /// <summary>CFG-01: sem login, nada de cadastro, alteração ou exclusão.</summary>
    private void AjustarBotoes()
    {
        var logado = _sessao.Autenticada;
        var configuracao = Selecionada();
        _nova.Enabled = _instancias.Enabled = logado;
        _editar.Enabled = _excluir.Enabled = _reiniciar.Enabled = logado && configuracao is not null;
        _pausar.Enabled = logado && configuracao?.Situacao == SituacaoConfiguracao.Ativa;
        _retomar.Enabled = logado && configuracao?.Situacao == SituacaoConfiguracao.Pausada;
    }

    private ConfiguracaoSincronizacao? Selecionada() =>
        _grade.SelectedRows.Count == 1 && _grade.SelectedRows[0].Tag is Guid id ? _configuracoes.FirstOrDefault(c => c.Id == id) : null;

    private void Editar(bool nova = false)
    {
        var existente = nova ? null : Selecionada();
        if (!nova && existente is null)
        {
            return;
        }

        if (_armazenamento.Carregar().Instancias.Count == 0)
        {
            Tela.Avisar(this, "Cadastre uma instância de conexão antes (botão \"Instâncias\").");
            return;
        }

        using var form = new FormConfiguracao(_armazenamento, _conexoes, _sessao, existente);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            // Inicia o laço de uma configuração nova ou que voltou a ficar ativa.
            _comandos.Recarregar();
            Atualizar();
        }
    }

    /// <summary>CFG-09: confirma e apaga a configuração e o checkpoint locais; nada vai para a API.</summary>
    private void Excluir()
    {
        if (Selecionada() is not { } configuracao)
        {
            return;
        }

        if (Tela.Confirmar(this, $"Excluir a configuração do tenant {configuracao.NomeTenant} (banco {configuracao.Banco})?{Environment.NewLine}" +
            "A configuração e o checkpoint locais serão removidos. Nada é excluído na plataforma."))
        {
            _armazenamento.ExcluirConfiguracao(configuracao.Id);
            Atualizar();
        }
    }

    /// <summary>CTL-03: confirmação com o novo período inicial (padrão 6 meses); o agendador espera o lote atual (CTL-06).</summary>
    private void Reiniciar()
    {
        if (Selecionada() is not { } configuracao)
        {
            return;
        }

        using var dialogo = new Form();
        Tela.Dialogo(dialogo, "Reiniciar sincronização", 480, 200);
        var periodo = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = _armazenamento.PeriodoInicialPadrao().ToDateTime(TimeOnly.MinValue),
        };
        var grade = Tela.Grade();
        Tela.Linha(grade, new Label
        {
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Text = $"Zerar o checkpoint de {configuracao.NomeTenant} (banco {configuracao.Banco}) e refazer a carga inicial? " +
                "Nenhuma venda é excluída na plataforma; as reenviadas são atualizadas.",
        });
        Tela.Linha(grade, "Novo período inicial", periodo);
        var confirmar = Tela.Botao("Reiniciar");
        confirmar.DialogResult = DialogResult.OK;
        var cancelar = Tela.Botao("Cancelar");
        cancelar.DialogResult = DialogResult.Cancel;
        dialogo.AcceptButton = confirmar;
        dialogo.CancelButton = cancelar;
        dialogo.Controls.Add(grade);
        dialogo.Controls.Add(Tela.Rodape(cancelar, confirmar));

        if (dialogo.ShowDialog(this) == DialogResult.OK)
        {
            _comandos.Reiniciar(configuracao.Id, DateOnly.FromDateTime(periodo.Value));
        }
    }

    private void Comandar(Action<ConfiguracaoSincronizacao> comando)
    {
        if (Selecionada() is { } configuracao)
        {
            comando(configuracao);
        }
    }

    private void Abrir(Form form)
    {
        using (form)
        {
            form.ShowDialog(this);
        }

        Atualizar();
    }

    /// <summary>Login (CFG-02); também chamado quando a API recusa a credencial e o agendador para tudo.</summary>
    private void PedirLogin()
    {
        if (_loginAberto)
        {
            return;
        }

        _loginAberto = true;
        try
        {
            Show();
            Activate();
            using var form = new FormLogin(_sessao, _armazenamento, _comandos);
            form.ShowDialog(this);
        }
        finally
        {
            _loginAberto = false;
        }

        Atualizar();
    }

    private void Coluna(string titulo, int peso) =>
        _grade.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = titulo, FillWeight = peso, SortMode = DataGridViewColumnSortMode.NotSortable });

    private static string Situacao(ConfiguracaoSincronizacao configuracao, EstadoExecucao? execucao, EstadoAgendamento? agendamento)
    {
        if (execucao?.NaoAutorizada == true)
        {
            return "Não autorizada";
        }

        if (agendamento?.Executando == true)
        {
            return configuracao.Situacao == SituacaoConfiguracao.Pausada ? "Pausando..." : "Executando";
        }

        return configuracao.Situacao.ToString();
    }

    private static string Intervalo(TimeSpan intervalo) => intervalo.TotalMinutes < 60
        ? $"{intervalo.TotalMinutes:0} min"
        : intervalo.Minutes == 0 ? $"{(int)intervalo.TotalHours} h" : $"{(int)intervalo.TotalHours} h {intervalo.Minutes} min";

    private static string Resultado(EstadoExecucao? execucao) => execucao?.UltimoResultado switch
    {
        null => "—",
        ResultadoExecucao.Sucesso => $"Sucesso em {execucao.UltimaExecucaoFim?.ToLocalTime():dd/MM HH:mm}",
        var resultado => string.IsNullOrEmpty(execucao.UltimaMensagem) ? $"{resultado}" : $"{resultado}: {execucao.UltimaMensagem}",
    };
}
