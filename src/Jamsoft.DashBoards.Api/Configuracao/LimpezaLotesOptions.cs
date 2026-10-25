namespace Jamsoft.DashBoards.Api.Configuracao;

/// <summary>Seção "LimpezaLotes": retenção de LOTE_INGESTAO (design, Risks: crescimento da tabela).</summary>
public sealed class LimpezaLotesOptions
{
    public const string Secao = "LimpezaLotes";
    public const int LoteMaximo = 5000;

    /// <summary>Desligada, a limpeza não roda (os testes de API usam assim).</summary>
    public bool Ativa { get; set; } = true;

    /// <summary>Espera entre uma limpeza e a próxima.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Lotes recebidos há mais que isto são apagados.</summary>
    public TimeSpan Retencao { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Linhas por DELETE. Máximo de 5.000.</summary>
    public int TamanhoLote { get; set; } = LoteMaximo;

    public bool Valida() => Intervalo > TimeSpan.Zero && Retencao > TimeSpan.Zero && TamanhoLote is > 0 and <= LoteMaximo;
}
