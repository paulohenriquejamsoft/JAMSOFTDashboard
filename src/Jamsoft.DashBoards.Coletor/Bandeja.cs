using Microsoft.Win32;

namespace Jamsoft.DashBoards.Coletor;

/// <summary>
/// Ícone na bandeja: abre a janela, liga ou desliga "Iniciar com o Windows" e sai. Fechar a janela só a esconde; o
/// agendador continua rodando até "Sair".
/// </summary>
internal sealed class Bandeja : ApplicationContext
{
    private readonly NotifyIcon _icone;
    private readonly Form _janela;
    private bool _saindo;

    public Bandeja(Form janela, bool minimizado)
    {
        _janela = janela;
        var iniciarComWindows = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true, Checked = InicioComWindows.Ativo };
        iniciarComWindows.CheckedChanged += (_, _) => InicioComWindows.Definir(iniciarComWindows.Checked);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => Abrir());
        menu.Items.Add(iniciarComWindows);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Sair());

        _icone = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Jamsoft DashBoards Coletor",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icone.DoubleClick += (_, _) => Abrir();

        janela.FormClosing += (_, e) =>
        {
            if (!_saindo && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                janela.Hide();
            }
        };
        // Desligamento do Windows ou "Sair": a janela fecha de verdade e o laço de mensagens termina.
        janela.FormClosed += (_, _) => Encerrar();

        if (!minimizado)
        {
            Abrir();
        }
    }

    public void Abrir()
    {
        _janela.Show();
        if (_janela.WindowState == FormWindowState.Minimized)
        {
            _janela.WindowState = FormWindowState.Normal;
        }

        _janela.Activate();
    }

    private void Sair()
    {
        _saindo = true;
        _janela.Close();
    }

    private void Encerrar()
    {
        _icone.Visible = false;
        _icone.Dispose();
        ExitThread();
    }
}

/// <summary>
/// "Iniciar com o Windows": valor na chave <c>HKCU\...\Run</c> do usuário, gravado só quando o técnico marca a opção.
/// </summary>
// SPEC_DEVIATION: o design lista "iniciar com o Windows" no config.json.
// Reason: a chave Run é a fonte da verdade; uma cópia no config.json poderia divergir dela sem nenhum ganho.
internal static class InicioComWindows
{
    public const string ArgumentoMinimizado = "--minimizado";

    private const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nome = "Jamsoft.DashBoards.Coletor";

    public static bool Ativo
    {
        get
        {
            using var chave = Registry.CurrentUser.OpenSubKey(Chave);
            return chave?.GetValue(Nome) is string;
        }
    }

    public static void Definir(bool ativo)
    {
        using var chave = Registry.CurrentUser.CreateSubKey(Chave);
        if (ativo)
        {
            chave.SetValue(Nome, $"\"{Environment.ProcessPath}\" {ArgumentoMinimizado}");
        }
        else
        {
            chave.DeleteValue(Nome, throwOnMissingValue: false);
        }
    }
}
