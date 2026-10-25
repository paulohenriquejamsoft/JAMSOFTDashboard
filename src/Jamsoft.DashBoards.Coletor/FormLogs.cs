using System.Text.Json;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;

namespace Jamsoft.DashBoards.Coletor;

/// <summary>Logs do Coletor com filtros por data, tenant, banco, rotina e resultado (LOG-05); disponível sem login (CFG-01).</summary>
internal sealed class FormLogs : Form
{
    private const string Todos = "(todos)";

    private readonly LeitorLogs _leitor;
    private readonly DateTimePicker _de = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _ate = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly ComboBox _tenant = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _banco = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 150 };
    private readonly ComboBox _rotina = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox _resultado = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button _filtrar;
    private readonly Label _situacao = new() { AutoSize = true, Margin = new Padding(12, 8, 3, 3) };
    private readonly DataGridView _grade = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
    };

    public FormLogs(LeitorLogs leitor, ArmazenamentoConfiguracao armazenamento)
    {
        _leitor = leitor;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Logs do Coletor";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Size = new Size(1150, 600);

        var hoje = DateTime.Today;
        _de.Value = hoje.AddDays(-7);
        _ate.Value = hoje;

        var configuracoes = armazenamento.Carregar().Configuracoes;
        _tenant.Items.Add(new OpcaoTenant(null, Todos));
        foreach (var tenant in configuracoes.DistinctBy(c => c.Tenant).OrderBy(c => c.NomeTenant))
        {
            _tenant.Items.Add(new OpcaoTenant(tenant.Tenant, tenant.NomeTenant));
        }

        _banco.Items.Add(Todos);
        _banco.Items.AddRange([.. configuracoes.Select(c => c.Banco).Distinct(StringComparer.OrdinalIgnoreCase).Order()]);
        _rotina.Items.AddRange([Todos, ConfiguracaoSincronizacao.RotinaVendas]);
        _resultado.Items.Add(Todos);
        _resultado.Items.AddRange([.. Enum.GetValues<ResultadoExecucao>().Cast<object>()]);
        _tenant.SelectedIndex = _banco.SelectedIndex = _rotina.SelectedIndex = _resultado.SelectedIndex = 0;

        _filtrar = Tela.Botao("Filtrar", async (_, _) => await FiltrarAsync());
        AcceptButton = _filtrar;
        var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        Filtro(filtros, "De", _de);
        Filtro(filtros, "Até", _ate);
        Filtro(filtros, "Tenant", _tenant);
        Filtro(filtros, "Banco", _banco);
        Filtro(filtros, "Rotina", _rotina);
        Filtro(filtros, "Resultado", _resultado);
        filtros.Controls.Add(_filtrar);
        filtros.Controls.Add(_situacao);

        Coluna(nameof(LinhaLog.DataHora), "Data/hora");
        Coluna(nameof(LinhaLog.Nivel), "Nível");
        Coluna(nameof(LinhaLog.Evento), "Evento");
        Coluna(nameof(LinhaLog.Tenant), "Tenant");
        Coluna(nameof(LinhaLog.Instancia), "Instância");
        Coluna(nameof(LinhaLog.Banco), "Banco");
        Coluna(nameof(LinhaLog.Rotina), "Rotina");
        Coluna(nameof(LinhaLog.Resultado), "Resultado");
        Coluna(nameof(LinhaLog.TipoErro), "Tipo de erro");
        Coluna(nameof(LinhaLog.Duracao), "Duração");
        Coluna(nameof(LinhaLog.Lidos), "Lidos");
        Coluna(nameof(LinhaLog.Enviados), "Enviados");
        Coluna(nameof(LinhaLog.Rejeitados), "Rejeitados");
        Coluna(nameof(LinhaLog.Mensagem), "Mensagem");
        Coluna(nameof(LinhaLog.Execucao), "Execução");

        Controls.Add(_grade);
        Controls.Add(filtros);
        Load += async (_, _) => await FiltrarAsync();
    }

    private async Task FiltrarAsync()
    {
        var de = DateOnly.FromDateTime(_de.Value);
        var ate = DateOnly.FromDateTime(_ate.Value);
        if (ate < de)
        {
            _situacao.Text = "A data final é anterior à inicial.";
            return;
        }

        var banco = _banco.Text.Trim();
        var filtro = new FiltroLogs(de, ate,
            (_tenant.SelectedItem as OpcaoTenant)?.Id,
            banco.Length == 0 || banco == Todos ? null : banco,
            _rotina.SelectedItem as string is { } rotina && rotina != Todos ? rotina : null,
            _resultado.SelectedItem as ResultadoExecucao?);

        _filtrar.Enabled = false;
        _situacao.Text = "Lendo os logs...";
        try
        {
            var eventos = await Task.Run(() => _leitor.Ler(filtro));
            _grade.DataSource = eventos.Select(LinhaLog.De).ToList();
            _situacao.Text = $"{eventos.Count} evento(s).";
        }
        catch (Exception erro) when (erro is JsonException or IOException or UnauthorizedAccessException)
        {
            // Uma linha corrompida (gravação interrompida, edição manual) não pode derrubar a tela.
            _grade.DataSource = null;
            _situacao.Text = "Não foi possível ler os logs do período: há um arquivo com linha inválida ou inacessível. Ajuste as datas para isolá-lo.";
        }
        finally
        {
            _filtrar.Enabled = true;
        }
    }

    private static void Filtro(FlowLayoutPanel painel, string rotulo, Control campo)
    {
        painel.Controls.Add(new Label { Text = rotulo, AutoSize = true, Margin = new Padding(9, 8, 3, 3) });
        painel.Controls.Add(campo);
    }

    private void Coluna(string propriedade, string titulo) =>
        _grade.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = propriedade, HeaderText = titulo, Name = propriedade });

    private sealed record OpcaoTenant(Guid? Id, string Nome)
    {
        public override string ToString() => Nome;
    }
}

/// <summary>Um evento do log já formatado para a grade.</summary>
internal sealed record LinhaLog(
    string DataHora,
    string Nivel,
    string Evento,
    string Tenant,
    string Instancia,
    string Banco,
    string Rotina,
    string Resultado,
    string TipoErro,
    string Duracao,
    string Lidos,
    string Enviados,
    string Rejeitados,
    string Mensagem,
    string Execucao)
{
    public static LinhaLog De(EventoLog e) => new(
        e.DataHora.ToString("dd/MM/yyyy HH:mm:ss"),
        e.Nivel switch { NivelLog.Informacao => "Informação", NivelLog.Aviso => "Aviso", _ => "Erro" },
        e.Evento,
        e.NomeTenant,
        e.Instancia,
        e.Banco,
        e.Rotina,
        e.Resultado?.ToString() ?? "",
        e.TipoErro switch
        {
            Nucleo.Logs.TipoErro.Conexao => "Conexão",
            Nucleo.Logs.TipoErro.Comunicacao => "Comunicação",
            { } tipo => tipo.ToString(),
            null => "",
        },
        e.Duracao?.ToString(@"hh\:mm\:ss") ?? "",
        e.Lidos?.ToString() ?? "",
        e.Enviados?.ToString() ?? "",
        e.Rejeitados?.ToString() ?? "",
        e.Mensagem ?? "",
        e.ExecucaoId.ToString()[..8]);
}
