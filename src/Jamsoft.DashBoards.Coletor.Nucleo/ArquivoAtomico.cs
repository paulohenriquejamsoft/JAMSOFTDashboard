namespace Jamsoft.DashBoards.Coletor.Nucleo;

/// <summary>
/// Gravação atômica dos arquivos de estado (AD-016): escreve num temporário ao lado, força para o disco e
/// troca pelo definitivo com <see cref="File.Replace(string, string, string?)"/>. Uma queda no meio deixa o
/// arquivo anterior intacto.
/// </summary>
public static class ArquivoAtomico
{
    public static void Gravar(string caminho, Action<Stream> escrever)
    {
        var temporario = caminho + ".tmp";
        try
        {
            using (var arquivo = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                escrever(arquivo);
                arquivo.Flush(flushToDisk: true);
            }

            if (File.Exists(caminho))
            {
                File.Replace(temporario, caminho, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporario, caminho);
            }
        }
        catch
        {
            File.Delete(temporario);
            throw;
        }
    }
}
