namespace Jamsoft.DashBoards.Coletor;

/// <summary>Montagem das telas em código (sem designer): grade de rótulo e campo, botões e mensagens.</summary>
internal static class Tela
{
    public const string Titulo = "Jamsoft DashBoards Coletor";

    /// <summary>Diálogo de tamanho fixo, escalado pelo DPI do monitor.</summary>
    public static void Dialogo(Form form, string titulo, int largura, int altura)
    {
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Text = titulo;
        form.Icon = SystemIcons.Application;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(largura, altura);
    }

    /// <summary>Duas colunas: rótulo à esquerda, campo ocupando o resto.</summary>
    public static TableLayoutPanel Grade()
    {
        var grade = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        return grade;
    }

    public static void Linha(TableLayoutPanel grade, string rotulo, Control campo)
    {
        grade.Controls.Add(new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 9, 3) });
        campo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        grade.Controls.Add(campo);
    }

    /// <summary>Campo sem rótulo, na coluna da direita (avisos, botões auxiliares).</summary>
    public static void Linha(TableLayoutPanel grade, Control campo)
    {
        grade.Controls.Add(new Label { AutoSize = true });
        campo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        grade.Controls.Add(campo);
    }

    public static Button Botao(string texto, EventHandler? clique = null)
    {
        var botao = new Button { Text = texto, AutoSize = true, MinimumSize = new Size(90, 28), Margin = new Padding(6, 3, 0, 3) };
        if (clique is not null)
        {
            botao.Click += clique;
        }

        return botao;
    }

    /// <summary>Barra de botões alinhada à direita, no rodapé.</summary>
    public static FlowLayoutPanel Rodape(params Control[] controles)
    {
        var rodape = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12, 6, 12, 12),
        };
        rodape.Controls.AddRange(controles);
        return rodape;
    }

    /// <summary>Mensagem de erro ou andamento abaixo dos campos.</summary>
    public static Label Mensagem() => new() { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = Color.Firebrick };

    public static TextBox Segredo() => new() { UseSystemPasswordChar = true };

    public static bool Confirmar(IWin32Window dono, string pergunta) =>
        MessageBox.Show(dono, pergunta, Titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
            == DialogResult.Yes;

    public static void Avisar(IWin32Window dono, string mensagem) =>
        MessageBox.Show(dono, mensagem, Titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
