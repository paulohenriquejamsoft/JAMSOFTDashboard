namespace Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;

public enum SituacaoConfiguracao
{
    Ativa,
    Pausada,
    Inativa,
}

/// <summary>
/// Vínculo de um banco do ERP a um tenant para uma rotina (CFG-05). <see cref="Id"/> é estável e vai para a API
/// como <c>configuracaoId</c>. <see cref="PeriodoInicial"/> nulo significa o padrão do CFG-07, calculado na carga
/// inicial com <see cref="ArmazenamentoConfiguracao.PeriodoInicialPadrao"/>.
/// </summary>
public sealed record ConfiguracaoSincronizacao(
    Guid Id,
    Guid Tenant,
    string NomeTenant,
    Guid InstanciaId,
    string Banco,
    string Rotina,
    TimeSpan Intervalo,
    DateOnly? PeriodoInicial,
    int JanelaReprocessamentoDias,
    SituacaoConfiguracao Situacao)
{
    public const string RotinaVendas = "Vendas";
    public const int JanelaPadraoDias = 7;
    public const int JanelaMaximaDias = 90;
    public static readonly TimeSpan IntervaloPadrao = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IntervaloMaximo = TimeSpan.FromHours(24);
}

/// <summary>Estado completo do <c>config.json</c>, com os segredos já decifrados.</summary>
public sealed record DadosColetor(
    CredencialApi? Credencial,
    IReadOnlyList<InstanciaConexao> Instancias,
    IReadOnlyList<ConfiguracaoSincronizacao> Configuracoes);

/// <summary>Configuração recusada; <see cref="Campo"/> é a propriedade em erro, para a tela marcar o campo.</summary>
public sealed class ConfiguracaoInvalidaException(string campo, string mensagem) : Exception(mensagem)
{
    public string Campo { get; } = campo;
}
