using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Jamsoft.DashBoards.Contratos.Coletor;

namespace Jamsoft.DashBoards.Coletor;

/// <summary>
/// Configuração de sincronização (CFG-05): tenant da lista autorizada, instância, banco, rotina, intervalo, período
/// inicial, janela de reprocessamento e situação. Quem valida é o Nucleo (CFG-05, CFG-06); a tela só marca o campo
/// indicado. Período inicial desmarcado = padrão de 6 meses calculado na carga inicial (CFG-07).
/// </summary>
internal sealed class FormConfiguracao : Form
{
    private readonly ArmazenamentoConfiguracao _armazenamento;
    private readonly ConexoesErp _conexoes;
    private readonly SessaoApi _sessao;
    private readonly ConfiguracaoSincronizacao? _existente;
    private readonly IReadOnlyList<InstanciaConexao> _instancias;
    private readonly ComboBox _tenant = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(TenantAutorizado.Nome) };
    private readonly ComboBox _instancia = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _banco = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _rotina = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _intervalo = new()
    {
        Minimum = (decimal)ConfiguracaoSincronizacao.IntervaloMinimo.TotalMinutes,
        Maximum = (decimal)ConfiguracaoSincronizacao.IntervaloMaximo.TotalMinutes,
        Value = (decimal)ConfiguracaoSincronizacao.IntervaloPadrao.TotalMinutes,
    };
    private readonly DateTimePicker _periodo = new() { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
    private readonly NumericUpDown _janela = new()
    {
        Minimum = 0,
        Maximum = ConfiguracaoSincronizacao.JanelaMaximaDias,
        Value = ConfiguracaoSincronizacao.JanelaPadraoDias,
    };
    private readonly ComboBox _situacao = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _mensagem = Tela.Mensagem();
    private readonly ErrorProvider _erros = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly Button _listarBancos;
    private readonly Button _salvar;

    public FormConfiguracao(ArmazenamentoConfiguracao armazenamento, ConexoesErp conexoes, SessaoApi sessao,
        ConfiguracaoSincronizacao? existente)
    {
        _armazenamento = armazenamento;
        _conexoes = conexoes;
        _sessao = sessao;
        _existente = existente;
        _instancias = armazenamento.Carregar().Instancias;
        _erros.ContainerControl = this;
        Tela.Dialogo(this, existente is null ? "Nova configuração de sincronização" : "Editar configuração de sincronização", 600, 480);

        foreach (var instancia in _instancias)
        {
            _instancia.Items.Add(FormInstancia.Descrever(instancia));
        }

        _rotina.Items.Add(ConfiguracaoSincronizacao.RotinaVendas);
        _rotina.SelectedIndex = 0;
        _situacao.Items.AddRange([SituacaoConfiguracao.Ativa, SituacaoConfiguracao.Inativa]);
        if (existente?.Situacao == SituacaoConfiguracao.Pausada)
        {
            _situacao.Items.Add(SituacaoConfiguracao.Pausada);
        }

        _situacao.SelectedItem = existente?.Situacao ?? SituacaoConfiguracao.Ativa;
        _periodo.Value = (existente?.PeriodoInicial ?? armazenamento.PeriodoInicialPadrao()).ToDateTime(TimeOnly.MinValue);
        if (existente is not null)
        {
            _instancia.SelectedIndex = IndiceInstancia(existente.InstanciaId);
            _banco.Text = existente.Banco;
            _rotina.SelectedItem = existente.Rotina;
            _intervalo.Value = Math.Clamp((decimal)existente.Intervalo.TotalMinutes, _intervalo.Minimum, _intervalo.Maximum);
            _periodo.Checked = existente.PeriodoInicial is not null;
            _janela.Value = Math.Clamp(existente.JanelaReprocessamentoDias, 0, ConfiguracaoSincronizacao.JanelaMaximaDias);
        }

        _listarBancos = Tela.Botao("Listar bancos", async (_, _) => await ListarBancosAsync());
        var grade = Tela.Grade();
        Tela.Linha(grade, "Tenant", _tenant);
        Tela.Linha(grade, "Instância", _instancia);
        Tela.Linha(grade, "Banco de origem", _banco);
        Tela.Linha(grade, _listarBancos);
        Tela.Linha(grade, "Rotina", _rotina);
        Tela.Linha(grade, "Intervalo (minutos)", _intervalo);
        Tela.Linha(grade, "Período inicial", _periodo);
        Tela.Linha(grade, new Label { Text = "Desmarcado: 6 meses antes da carga inicial.", AutoSize = true, ForeColor = SystemColors.GrayText });
        Tela.Linha(grade, "Janela de reprocessamento (dias)", _janela);
        Tela.Linha(grade, "Situação", _situacao);
        Tela.Linha(grade, _mensagem);
        _listarBancos.Anchor = AnchorStyles.Left;

        _salvar = Tela.Botao("Salvar", (_, _) => Salvar());
        var cancelar = Tela.Botao("Cancelar", (_, _) => Close());
        AcceptButton = _salvar;
        CancelButton = cancelar;
        Controls.Add(grade);
        Controls.Add(Tela.Rodape(cancelar, _salvar));
        Load += async (_, _) => await CarregarTenantsAsync();
    }

    /// <summary>Tenants da lista autorizada (CFG-05), atualizada pela API; o da configuração editada fica sempre na lista.</summary>
    private async Task CarregarTenantsAsync()
    {
        _salvar.Enabled = false;
        _mensagem.ForeColor = SystemColors.ControlText;
        _mensagem.Text = "Carregando tenants autorizados...";
        var tenants = (await _sessao.AtualizarTenantsAsync()).ToList();
        if (_existente is not null && tenants.All(t => t.IdPublico != _existente.Tenant))
        {
            tenants.Insert(0, new TenantAutorizado(_existente.Tenant, _existente.NomeTenant));
        }

        _tenant.Items.Clear();
        _tenant.Items.AddRange([.. tenants]);
        _tenant.SelectedItem = tenants.FirstOrDefault(t => t.IdPublico == _existente?.Tenant);
        _mensagem.Text = tenants.Count == 0 ? "Nenhum tenant autorizado para esta credencial." : "";
        _salvar.Enabled = true;
    }

    private async Task ListarBancosAsync()
    {
        if (_instancia.SelectedIndex < 0)
        {
            Marcar(nameof(ConfiguracaoSincronizacao.InstanciaId), "Escolha a instância.");
            return;
        }

        _listarBancos.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var resultado = await _conexoes.TestarAsync(_instancias[_instancia.SelectedIndex]);
            if (!resultado.Sucesso)
            {
                Marcar(nameof(ConfiguracaoSincronizacao.InstanciaId), resultado.Mensagem ?? "Falha ao conectar.");
                return;
            }

            var digitado = _banco.Text;
            _banco.Items.Clear();
            _banco.Items.AddRange([.. resultado.Bancos]);
            _banco.Text = digitado;
            _banco.DroppedDown = true;
        }
        finally
        {
            _listarBancos.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void Salvar()
    {
        _erros.Clear();
        _mensagem.Text = "";
        var tenant = _tenant.SelectedItem as TenantAutorizado;
        var configuracao = new ConfiguracaoSincronizacao(
            _existente?.Id ?? Guid.NewGuid(),
            tenant?.IdPublico ?? Guid.Empty,
            tenant?.Nome ?? "",
            _instancia.SelectedIndex < 0 ? Guid.Empty : _instancias[_instancia.SelectedIndex].Id,
            _banco.Text.Trim(),
            _rotina.SelectedItem as string ?? "",
            TimeSpan.FromMinutes((double)_intervalo.Value),
            _periodo.Checked ? DateOnly.FromDateTime(_periodo.Value) : null,
            (int)_janela.Value,
            (SituacaoConfiguracao)_situacao.SelectedItem!);
        try
        {
            _armazenamento.SalvarConfiguracao(configuracao);
        }
        catch (ConfiguracaoInvalidaException erro)
        {
            Marcar(erro.Campo, erro.Message);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Mostra a mensagem do Nucleo ao lado do campo indicado por <see cref="ConfiguracaoInvalidaException.Campo"/>.</summary>
    private void Marcar(string campo, string mensagem)
    {
        Control controle = campo switch
        {
            nameof(ConfiguracaoSincronizacao.Tenant) => _tenant,
            nameof(ConfiguracaoSincronizacao.InstanciaId) => _instancia,
            nameof(ConfiguracaoSincronizacao.Banco) => _banco,
            nameof(ConfiguracaoSincronizacao.Rotina) => _rotina,
            nameof(ConfiguracaoSincronizacao.Intervalo) => _intervalo,
            nameof(ConfiguracaoSincronizacao.JanelaReprocessamentoDias) => _janela,
            _ => _mensagem,
        };
        _erros.SetError(controle, mensagem);
        _mensagem.ForeColor = Color.Firebrick;
        _mensagem.Text = mensagem;
        controle.Focus();
    }

    private int IndiceInstancia(Guid id)
    {
        for (var i = 0; i < _instancias.Count; i++)
        {
            if (_instancias[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
