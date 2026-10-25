namespace Jamsoft.DashBoards.Coletor;

internal static class Program
{
    /// <summary>Ponto de entrada da casca WinForms; o Generic Host e a bandeja entram no T35.</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
    }
}
