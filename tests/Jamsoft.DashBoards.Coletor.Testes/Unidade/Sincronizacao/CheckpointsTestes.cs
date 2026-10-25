using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

namespace Jamsoft.DashBoards.Coletor.Testes.Unidade.Sincronizacao;

/// <summary>Checkpoint persistido por (configuração, rotina) (SYNC-03, SYNC-09).</summary>
[Trait("Categoria", "Unidade")]
public sealed class CheckpointsTestes : IDisposable
{
    private const string Vendas = "Vendas";
    private readonly PastaTemporaria _pasta = new();

    public void Dispose() => _pasta.Dispose();

    [Fact]
    public void Avancar_grava_no_arquivo_e_outra_instancia_rele_o_ultimo_checkpoint()
    {
        var configuracao = Guid.NewGuid();
        var checkpoints = new Checkpoints(_pasta.Caminho);

        checkpoints.Avancar(configuracao, Vendas, new Checkpoint(new DateTime(2026, 5, 1), 100));
        checkpoints.Avancar(configuracao, Vendas, new Checkpoint(new DateTime(2026, 5, 3), 250));

        Assert.True(File.Exists(_pasta.Arquivo(Checkpoints.NomeArquivo)));
        Assert.Equal(new Checkpoint(new DateTime(2026, 5, 3), 250), new Checkpoints(_pasta.Caminho).Obter(configuracao, Vendas));
    }

    [Fact]
    public void Configuracoes_e_rotinas_diferentes_tem_checkpoints_independentes()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var checkpoints = new Checkpoints(_pasta.Caminho);

        checkpoints.Avancar(a, Vendas, new Checkpoint(new DateTime(2026, 5, 1), 10));
        checkpoints.Avancar(b, Vendas, new Checkpoint(new DateTime(2026, 6, 1), 20));
        checkpoints.Avancar(a, "Outra", new Checkpoint(new DateTime(2026, 7, 1), 30));
        checkpoints.Avancar(a, Vendas, new Checkpoint(new DateTime(2026, 5, 2), 11));

        var relido = new Checkpoints(_pasta.Caminho);
        Assert.Equal(new Checkpoint(new DateTime(2026, 5, 2), 11), relido.Obter(a, Vendas));
        Assert.Equal(new Checkpoint(new DateTime(2026, 6, 1), 20), relido.Obter(b, Vendas));
        Assert.Equal(new Checkpoint(new DateTime(2026, 7, 1), 30), relido.Obter(a, "Outra"));
    }

    [Fact]
    public void Zerar_uma_configuracao_nao_afeta_outra()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var checkpoints = new Checkpoints(_pasta.Caminho);
        checkpoints.Avancar(a, Vendas, new Checkpoint(new DateTime(2026, 5, 1), 10));
        checkpoints.Avancar(b, Vendas, new Checkpoint(new DateTime(2026, 6, 1), 20));

        checkpoints.Zerar(a, Vendas);

        var relido = new Checkpoints(_pasta.Caminho);
        Assert.Null(relido.Obter(a, Vendas));
        Assert.Equal(new Checkpoint(new DateTime(2026, 6, 1), 20), relido.Obter(b, Vendas));
    }

    [Fact]
    public void Arquivo_ausente_significa_sem_checkpoint()
    {
        Assert.False(File.Exists(_pasta.Arquivo(Checkpoints.NomeArquivo)));

        Assert.Null(new Checkpoints(_pasta.Caminho).Obter(Guid.NewGuid(), Vendas));
    }
}
