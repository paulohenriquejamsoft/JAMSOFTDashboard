using System.Text.Json;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Coletor.Testes.Unidade.Logs;

/// <summary>Logs diários do Coletor em JSON lines (LOG-01..LOG-06).</summary>
[Trait("Categoria", "Unidade")]
public sealed class LogExecucoesTestes : IDisposable
{
    private readonly PastaTemporaria _pasta = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly ContextoExecucao _contexto = new(Guid.NewGuid(), Guid.NewGuid(), "Loja A", "SRV01,1433", "SGE_LOJA", "Vendas");

    public void Dispose() => _pasta.Dispose();

    private LogExecucoes Log() => new(_pasta.Caminho, _relogio);

    private string[] Linhas(string arquivo) => File.ReadAllLines(Path.Combine(_pasta.Caminho, arquivo));

    private static JsonElement Json(string linha) => JsonDocument.Parse(linha).RootElement;

    [Fact]
    public void Grava_um_arquivo_por_dia()
    {
        var log = Log();

        log.Registrar(_contexto, NivelLog.Informacao, "execucao_inicio", "Início");
        log.Registrar(_contexto, NivelLog.Informacao, "lote_enviado", "Lote 1");
        _relogio.Advance(TimeSpan.FromDays(1));
        log.Registrar(_contexto, NivelLog.Informacao, "execucao_inicio", "Início");

        Assert.Equal(["coletor-20261025.jsonl", "coletor-20261026.jsonl"],
            Directory.GetFiles(_pasta.Caminho).Select(Path.GetFileName).Order());
        Assert.Equal(2, Linhas("coletor-20261025.jsonl").Length);
        Assert.Single(Linhas("coletor-20261026.jsonl"));
    }

    [Fact]
    public void Cada_linha_traz_data_hora_tenant_instancia_banco_rotina_execucao_e_nivel()
    {
        Log().Registrar(_contexto, NivelLog.Aviso, "data_hora_nula", "Venda sem DataHoraFechamento");

        var linha = Json(Assert.Single(Linhas("coletor-20261025.jsonl")));

        Assert.Equal(_relogio.GetLocalNow(), linha.GetProperty("dataHora").GetDateTimeOffset());
        Assert.Equal(_contexto.Tenant, linha.GetProperty("tenant").GetGuid());
        Assert.Equal("Loja A", linha.GetProperty("nomeTenant").GetString());
        Assert.Equal("SRV01,1433", linha.GetProperty("instancia").GetString());
        Assert.Equal("SGE_LOJA", linha.GetProperty("banco").GetString());
        Assert.Equal("Vendas", linha.GetProperty("rotina").GetString());
        Assert.Equal(_contexto.ExecucaoId, linha.GetProperty("execucaoId").GetGuid());
        Assert.Equal("Aviso", linha.GetProperty("nivel").GetString());
        Assert.Equal("data_hora_nula", linha.GetProperty("evento").GetString());
    }

    [Fact]
    public void Evento_de_fim_traz_inicio_termino_duracao_contagens_e_resultado()
    {
        var inicio = _relogio.GetLocalNow();
        _relogio.Advance(TimeSpan.FromSeconds(95));

        Log().RegistrarFim(_contexto, inicio, lidos: 1200, enviados: 1198, rejeitados: 2, ResultadoExecucao.Sucesso);

        var linha = Json(Assert.Single(Linhas("coletor-20261025.jsonl")));
        Assert.Equal("execucao_fim", linha.GetProperty("evento").GetString());
        Assert.Equal(inicio, linha.GetProperty("inicio").GetDateTimeOffset());
        Assert.Equal(inicio.AddSeconds(95), linha.GetProperty("fim").GetDateTimeOffset());
        Assert.Equal("00:01:35", linha.GetProperty("duracao").GetString());
        Assert.Equal(1200, linha.GetProperty("lidos").GetInt32());
        Assert.Equal(1198, linha.GetProperty("enviados").GetInt32());
        Assert.Equal(2, linha.GetProperty("rejeitados").GetInt32());
        Assert.Equal("Sucesso", linha.GetProperty("resultado").GetString());
    }

    [Fact]
    public void Erro_e_classificado_em_uma_das_4_categorias_com_a_mensagem_tecnica()
    {
        var log = Log();

        log.RegistrarErro(_contexto, TipoErro.Conexao, "A network-related error occurred");
        log.RegistrarErro(_contexto, TipoErro.Consulta, "Execution Timeout Expired");
        log.RegistrarErro(_contexto, TipoErro.Comunicacao, "Connection refused (api:443)");
        log.RegistrarErro(_contexto, TipoErro.Processamento, "Lote recusado: 413");

        var linhas = Linhas("coletor-20261025.jsonl").Select(Json).ToList();
        Assert.Equal(["Conexão", "Consulta", "Comunicação", "Processamento"], linhas.Select(l => l.GetProperty("tipoErro").GetString()));
        Assert.Equal(
            ["A network-related error occurred", "Execution Timeout Expired", "Connection refused (api:443)", "Lote recusado: 413"],
            linhas.Select(l => l.GetProperty("mensagem").GetString()));
        Assert.All(linhas, l => Assert.Equal("Erro", l.GetProperty("nivel").GetString()));
    }

    [Fact]
    public void Mensagem_com_Password_ou_Pwd_sai_sem_a_senha()
    {
        var log = Log();

        log.RegistrarErro(_contexto, TipoErro.Conexao, "Falha em Server=SRV01;User Id=leitor;Password=Segredo123;Encrypt=True");
        log.Registrar(_contexto, NivelLog.Aviso, "aviso", "pwd = OutroSegredo; Database=SGE");

        var conteudo = File.ReadAllText(Path.Combine(_pasta.Caminho, "coletor-20261025.jsonl"));
        Assert.DoesNotContain("Segredo123", conteudo, StringComparison.Ordinal);
        Assert.DoesNotContain("OutroSegredo", conteudo, StringComparison.Ordinal);
        var mensagens = Linhas("coletor-20261025.jsonl").Select(l => Json(l).GetProperty("mensagem").GetString()).ToList();
        Assert.Contains("Falha em Server=SRV01;User Id=leitor;", mensagens[0], StringComparison.Ordinal);
        Assert.Contains("Encrypt=True", mensagens[0], StringComparison.Ordinal);
        Assert.Contains("Database=SGE", mensagens[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Leitor_filtra_por_data_tenant_banco_rotina_e_resultado()
    {
        var log = Log();
        var tenantB = Guid.NewGuid();
        var falhaA = _contexto with { ExecucaoId = Guid.NewGuid() };
        var sucessoB = new ContextoExecucao(Guid.NewGuid(), tenantB, "Loja B", "SRV02", "SGE_B", "Vendas");
        var outraRotina = _contexto with { ExecucaoId = Guid.NewGuid(), Rotina = "Outra" };

        log.RegistrarErro(falhaA, TipoErro.Conexao, "sem rede");
        log.RegistrarFim(falhaA, _relogio.GetLocalNow(), 0, 0, 0, ResultadoExecucao.Falha);
        log.RegistrarFim(outraRotina, _relogio.GetLocalNow(), 1, 1, 0, ResultadoExecucao.Sucesso);
        _relogio.Advance(TimeSpan.FromDays(1));
        log.Registrar(sucessoB, NivelLog.Informacao, "execucao_inicio", "Início");
        log.RegistrarFim(sucessoB, _relogio.GetLocalNow(), 5, 5, 0, ResultadoExecucao.Sucesso);

        var leitor = new LeitorLogs(_pasta.Caminho);
        var dia1 = new DateOnly(2026, 10, 25);
        var dia2 = new DateOnly(2026, 10, 26);
        static Guid[] Execucoes(IEnumerable<EventoLog> eventos) => [.. eventos.Select(e => e.ExecucaoId).Distinct().Order()];

        Assert.Equal(5, leitor.Ler(new FiltroLogs(dia1, dia2)).Count);
        Assert.Equal(new[] { falhaA.ExecucaoId, outraRotina.ExecucaoId }.Order(), Execucoes(leitor.Ler(new FiltroLogs(dia1, dia1))));
        Assert.Equal([sucessoB.ExecucaoId], Execucoes(leitor.Ler(new FiltroLogs(dia1, dia2, Tenant: tenantB))));
        Assert.Equal([sucessoB.ExecucaoId], Execucoes(leitor.Ler(new FiltroLogs(dia1, dia2, Banco: "sge_b"))));
        Assert.Equal([outraRotina.ExecucaoId], Execucoes(leitor.Ler(new FiltroLogs(dia1, dia2, Rotina: "Outra"))));

        var falhas = leitor.Ler(new FiltroLogs(dia1, dia2, Resultado: ResultadoExecucao.Falha));
        Assert.Equal(["erro", "execucao_fim"], falhas.Select(e => e.Evento));
        Assert.All(falhas, e => Assert.Equal(falhaA.ExecucaoId, e.ExecucaoId));
        Assert.Equal(TipoErro.Conexao, falhas[0].TipoErro);
    }

    [Fact]
    public void Arquivos_com_mais_de_30_dias_sao_apagados_e_os_mais_novos_mantidos()
    {
        foreach (var dias in new[] { 31, 30, 29 })
        {
            var data = DateOnly.FromDateTime(_relogio.GetLocalNow().DateTime).AddDays(-dias);
            File.WriteAllText(Path.Combine(_pasta.Caminho, $"coletor-{data:yyyyMMdd}.jsonl"), "{}\n");
        }

        Log().LimparAntigos();

        Assert.Equal(["coletor-20260925.jsonl", "coletor-20260926.jsonl"],
            Directory.GetFiles(_pasta.Caminho).Select(Path.GetFileName).Order());
    }
}
