namespace Jamsoft.DashBoards.Coletor;

/// <summary>Janela principal do Coletor; a grade de configurações entra no T37.</summary>
internal sealed class FormPrincipal : Form
{
    public FormPrincipal()
    {
        Text = "Jamsoft DashBoards Coletor";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 560);
    }
}
