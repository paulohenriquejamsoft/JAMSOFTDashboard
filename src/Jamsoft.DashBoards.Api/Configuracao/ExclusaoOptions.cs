namespace Jamsoft.DashBoards.Api.Configuracao;

/// <summary>Seção "Exclusao": rotina de exclusão de tenant (DEL-04, DEL-06).</summary>
public sealed class ExclusaoOptions
{
    public const string Secao = "Exclusao";

    /// <summary>Desligado, o worker não processa jobs (os testes de API usam assim).</summary>
    public bool WorkerAtivo { get; set; } = true;

    /// <summary>Espera entre buscas de job quando não há nada a fazer.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Linhas por DELETE (cada lote é uma transação). Máximo de 5.000 (DEL-04).</summary>
    public int TamanhoLote { get; set; } = 5000;

    /// <summary>Pausa entre lotes, para não segurar locks de outros tenants.</summary>
    public TimeSpan PausaEntreLotes { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Job EmExecucao sem heartbeat há mais que isto é retomado (API reiniciada no meio).</summary>
    public TimeSpan HeartbeatExpiraEm { get; set; } = TimeSpan.FromMinutes(2);

    public const int LoteMaximo = 5000;

    public bool Valida() => Intervalo > TimeSpan.Zero && TamanhoLote is > 0 and <= LoteMaximo
        && PausaEntreLotes >= TimeSpan.Zero && HeartbeatExpiraEm > TimeSpan.Zero;
}
