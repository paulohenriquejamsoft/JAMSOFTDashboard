namespace Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

/// <summary>Pasta de dados descartável por teste; nunca toca ProgramData/AppData.</summary>
public sealed class PastaTemporaria : IDisposable
{
    public string Caminho { get; } = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "JD_Coletor_" + Guid.NewGuid().ToString("N"))).FullName;

    public string Arquivo(string nome) => Path.Combine(Caminho, nome);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Caminho, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
