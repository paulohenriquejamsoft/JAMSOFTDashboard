using System.Threading.Channels;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;

/// <summary>Pedido da tela para o <see cref="Agendador"/>.</summary>
public abstract record ComandoConfiguracao;

/// <summary>CTL-01: conclui o lote em andamento, não inicia novas execuções e grava a situação "Pausada".</summary>
public sealed record PausarConfiguracao(Guid ConfiguracaoId) : ComandoConfiguracao;

/// <summary>CTL-02: grava "Ativa" e volta a agendar a partir do checkpoint preservado.</summary>
public sealed record RetomarConfiguracao(Guid ConfiguracaoId) : ComandoConfiguracao;

/// <summary>
/// CTL-03/04/06: depois do lote em andamento, zera só o checkpoint desta configuração, grava o novo período inicial
/// (nulo = padrão de 6 meses, calculado agora) e executa uma nova carga inicial.
/// </summary>
public sealed record ReiniciarConfiguracao(Guid ConfiguracaoId, DateOnly? NovoPeriodoInicial) : ComandoConfiguracao;

/// <summary>Inicia as configurações ativas ainda sem laço: uma recém-cadastrada ou todas depois de um novo login.</summary>
public sealed record RecarregarConfiguracoes : ComandoConfiguracao;

/// <summary>
/// Fila de comandos da tela para o agendador (<see cref="Channel{T}"/>): a tela só enfileira; o agendador aplica na
/// ordem, na thread dele. Singleton compartilhado entre a tela e o <see cref="Agendador"/>.
/// </summary>
public sealed class ComandosConfiguracao
{
    private readonly Channel<ComandoConfiguracao> _canal =
        Channel.CreateUnbounded<ComandoConfiguracao>(new UnboundedChannelOptions { SingleReader = true });

    internal ChannelReader<ComandoConfiguracao> Leitor => _canal.Reader;

    public void Pausar(Guid configuracaoId) => Enviar(new PausarConfiguracao(configuracaoId));

    public void Retomar(Guid configuracaoId) => Enviar(new RetomarConfiguracao(configuracaoId));

    /// <summary>A tela pede a confirmação antes (CTL-03). Nenhuma venda é apagada na plataforma (CTL-05).</summary>
    public void Reiniciar(Guid configuracaoId, DateOnly? novoPeriodoInicial = null) =>
        Enviar(new ReiniciarConfiguracao(configuracaoId, novoPeriodoInicial));

    public void Recarregar() => Enviar(new RecarregarConfiguracoes());

    private void Enviar(ComandoConfiguracao comando) => _canal.Writer.TryWrite(comando);
}
