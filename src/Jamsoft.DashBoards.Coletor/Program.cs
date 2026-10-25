using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jamsoft.DashBoards.Coletor;

internal static class Program
{
    /// <summary>Pasta de dados (AD-016): <c>%ProgramData%\Jamsoft\DashBoards.Coletor</c>.</summary>
    public static readonly string PastaDados =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Jamsoft", "DashBoards.Coletor");

    private static readonly TimeSpan TempoParaParar = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Generic Host com o Nucleo e o <see cref="Agendador"/> em background enquanto o app está aberto; a janela vive na
    /// bandeja. "Sair" encerra o laço de mensagens e depois o host, que espera os lotes em andamento.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Duas instâncias na mesma máquina executariam a mesma configuração duas vezes (SCH-02).
        using var unica = new Mutex(true, @"Global\Jamsoft.DashBoards.Coletor", out var primeira);
        if (!primeira)
        {
            MessageBox.Show("O Coletor já está aberto. Use o ícone na bandeja.", "Jamsoft DashBoards Coletor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Directory.CreateDirectory(PastaDados);
        var construtor = Host.CreateApplicationBuilder();
        Registrar(construtor.Services, PastaDados);
        using var host = construtor.Build();

        // Antes do agendador: com a credencial gravada, as configurações ativas retomam sem esperar o login (SCH-05).
        if (host.Services.GetRequiredService<ArmazenamentoConfiguracao>().Carregar().Credencial is { } credencial)
        {
            host.Services.GetRequiredService<SessaoApi>().Usar(credencial);
        }

        host.Start();
        var janela = host.Services.GetRequiredService<FormPrincipal>();
        Application.Run(new Bandeja(janela, args.Contains(InicioComWindows.ArgumentoMinimizado)));
        host.StopAsync(TempoParaParar).GetAwaiter().GetResult();
    }

    private static void Registrar(IServiceCollection servicos, string pasta)
    {
        var pastaLogs = Path.Combine(pasta, "logs");
        servicos.AddSingleton(TimeProvider.System);
        servicos.AddSingleton(new Checkpoints(pasta));
        servicos.AddSingleton(new RegistroExecucoes(pasta));
        servicos.AddSingleton(new LeitorLogs(pastaLogs));
        servicos.AddSingleton(new ConexoesErp());
        servicos.AddSingleton<ComandosConfiguracao>();
        servicos.AddSingleton<SessaoApi>();
        servicos.AddSingleton(sp => new LogExecucoes(pastaLogs, sp.GetRequiredService<TimeProvider>()));
        servicos.AddSingleton(sp => new ArmazenamentoConfiguracao(pasta, sp.GetRequiredService<Checkpoints>(),
            sp.GetRequiredService<TimeProvider>()));
        servicos.AddSingleton<IExecutorSincronizacao>(sp =>
        {
            var conexoes = sp.GetRequiredService<ConexoesErp>();
            return new ExecutorSincronizacao(
                sp.GetRequiredService<ArmazenamentoConfiguracao>(),
                sp.GetRequiredService<Checkpoints>(),
                sp.GetRequiredService<RegistroExecucoes>(),
                sp.GetRequiredService<LogExecucoes>(),
                sp.GetRequiredService<SessaoApi>(),
                (instancia, banco) => new ExtratorVendas(conexoes.ConnectionString(instancia, banco)),
                sp.GetRequiredService<TimeProvider>());
        });
        servicos.AddSingleton(sp => new Agendador(
            sp.GetRequiredService<ArmazenamentoConfiguracao>(),
            sp.GetRequiredService<Checkpoints>(),
            sp.GetRequiredService<IExecutorSincronizacao>(),
            sp.GetRequiredService<ComandosConfiguracao>(),
            sp.GetRequiredService<TimeProvider>()));
        servicos.AddHostedService(sp => sp.GetRequiredService<Agendador>());
        servicos.AddHostedService<LimpezaLogs>();
        servicos.AddSingleton<FormPrincipal>();
    }
}

/// <summary>Retenção de 30 dias dos logs (LOG-06): limpa ao iniciar e uma vez por dia.</summary>
internal sealed class LimpezaLogs(LogExecucoes log, TimeProvider relogio) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var diario = new PeriodicTimer(TimeSpan.FromDays(1), relogio);
        do
        {
            try
            {
                log.LimparAntigos();
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                // Arquivo preso por outro processo: fica para a limpeza de amanhã.
            }
        }
        while (await diario.WaitForNextTickAsync(stoppingToken));
    }
}
