using Jamsoft.DashBoards.Coletor.Nucleo;
using Jamsoft.DashBoards.Coletor.Nucleo.Configuracao;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Coletor.Testes.Unidade.Configuracao;

/// <summary><c>config.json</c> do Coletor (CFG-02, CFG-05..CFG-09).</summary>
[Trait("Categoria", "Unidade")]
public sealed class ArmazenamentoConfiguracaoTestes : IDisposable
{
    private const string SenhaErp = "Senha-Do-ERP-123";
    private const string ClientSecret = "q1W2e3R4t5Y6u7I8o9P0a1S2d3F4g5H6j7K8l9Z0x1C";

    private readonly PastaTemporaria _pasta = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero));
    private readonly InstanciaConexao _instancia = new(Guid.NewGuid(), "SRV01", 1433, TipoAutenticacao.Sql, "leitor", SenhaErp);

    public void Dispose() => _pasta.Dispose();

    private ArmazenamentoConfiguracao Novo() => new(_pasta.Caminho, new Checkpoints(_pasta.Caminho), _relogio);

    private ConfiguracaoSincronizacao Configuracao(Guid? tenant = null, string nomeTenant = "Loja A", string banco = "SGE_LOJA") => new(
        Id: Guid.NewGuid(),
        Tenant: tenant ?? Guid.NewGuid(),
        NomeTenant: nomeTenant,
        InstanciaId: _instancia.Id,
        Banco: banco,
        Rotina: ConfiguracaoSincronizacao.RotinaVendas,
        Intervalo: TimeSpan.FromMinutes(15),
        PeriodoInicial: new DateOnly(2026, 4, 1),
        JanelaReprocessamentoDias: 7,
        Situacao: SituacaoConfiguracao.Ativa);

    private ArmazenamentoConfiguracao ComInstancia()
    {
        var armazenamento = Novo();
        armazenamento.SalvarInstancia(_instancia);
        return armazenamento;
    }

    [Fact]
    public void Arquivo_gravado_nao_contem_senha_nem_client_secret_em_texto()
    {
        var armazenamento = ComInstancia();
        armazenamento.SalvarCredencial(new CredencialApi("https://api.exemplo", Guid.NewGuid(), ClientSecret));
        armazenamento.SalvarConfiguracao(Configuracao());

        var conteudo = File.ReadAllText(_pasta.Arquivo(ArmazenamentoConfiguracao.NomeArquivo));

        Assert.Contains("SRV01", conteudo, StringComparison.Ordinal);
        Assert.DoesNotContain(SenhaErp, conteudo, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientSecret, conteudo, StringComparison.Ordinal);
    }

    [Fact]
    public void Recarrega_igual_ao_que_foi_gravado()
    {
        var credencial = new CredencialApi("https://api.exemplo", Guid.NewGuid(), ClientSecret);
        var configuracao = Configuracao() with { PeriodoInicial = null, Situacao = SituacaoConfiguracao.Pausada };
        var windows = new InstanciaConexao(Guid.NewGuid(), @"SRV02\SQLEXPRESS", null, TipoAutenticacao.Windows, null, null);
        var armazenamento = ComInstancia();
        armazenamento.SalvarInstancia(windows);
        armazenamento.SalvarCredencial(credencial);
        armazenamento.SalvarConfiguracao(configuracao);

        var dados = Novo().Carregar();

        Assert.Equal(credencial, dados.Credencial);
        Assert.Equal([_instancia, windows], dados.Instancias);
        Assert.Equal([configuracao], dados.Configuracoes);
    }

    [Fact]
    public void Intervalo_fora_de_5_min_a_24_h_e_janela_fora_de_0_a_90_dias_sao_recusados()
    {
        var armazenamento = ComInstancia();

        Assert.Equal("Intervalo", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao() with { Intervalo = TimeSpan.FromMinutes(4) })).Campo);
        Assert.Equal("Intervalo", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao() with { Intervalo = TimeSpan.FromHours(25) })).Campo);
        Assert.Equal("JanelaReprocessamentoDias", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao() with { JanelaReprocessamentoDias = 91 })).Campo);
        Assert.Equal("JanelaReprocessamentoDias", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao() with { JanelaReprocessamentoDias = -1 })).Campo);
        Assert.Empty(Novo().Carregar().Configuracoes);

        armazenamento.SalvarConfiguracao(Configuracao() with { Intervalo = TimeSpan.FromMinutes(5), JanelaReprocessamentoDias = 0 });
        armazenamento.SalvarConfiguracao(Configuracao(banco: "OUTRO") with { Intervalo = TimeSpan.FromHours(24), JanelaReprocessamentoDias = 90 });
        Assert.Equal(2, Novo().Carregar().Configuracoes.Count);
    }

    [Fact]
    public void Campos_obrigatorios_da_configuracao_sao_exigidos()
    {
        var armazenamento = ComInstancia();

        Assert.Equal("Tenant", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao(tenant: Guid.Empty))).Campo);
        Assert.Equal("InstanciaId", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao() with { InstanciaId = Guid.NewGuid() })).Campo);
        Assert.Equal("Banco", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao(banco: " "))).Campo);
        Assert.Equal("Rotina", Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao() with { Rotina = "Compras" })).Campo);
        Assert.Empty(Novo().Carregar().Configuracoes);
    }

    [Fact]
    public void Banco_ja_vinculado_a_outro_tenant_na_mesma_rotina_e_recusado()
    {
        var armazenamento = ComInstancia();
        armazenamento.SalvarConfiguracao(Configuracao(nomeTenant: "Loja A", banco: "SGE_LOJA"));

        var erro = Assert.Throws<ConfiguracaoInvalidaException>(
            () => armazenamento.SalvarConfiguracao(Configuracao(nomeTenant: "Loja B", banco: "sge_loja")));

        Assert.Equal("Banco", erro.Campo);
        Assert.Equal("Banco já vinculado ao tenant Loja A", erro.Message);
        Assert.Single(Novo().Carregar().Configuracoes);
    }

    [Fact]
    public void Periodo_inicial_padrao_e_hoje_menos_6_meses()
    {
        Assert.Equal(new DateOnly(2026, 4, 25), Novo().PeriodoInicialPadrao());

        _relogio.Advance(TimeSpan.FromDays(10));

        Assert.Equal(new DateOnly(2026, 5, 4), Novo().PeriodoInicialPadrao());
    }

    [Fact]
    public void Excluir_remove_a_configuracao_e_o_seu_checkpoint()
    {
        var checkpoints = new Checkpoints(_pasta.Caminho);
        var armazenamento = new ArmazenamentoConfiguracao(_pasta.Caminho, checkpoints, _relogio);
        armazenamento.SalvarInstancia(_instancia);
        var excluida = Configuracao(banco: "SGE_A");
        var mantida = Configuracao(banco: "SGE_B");
        armazenamento.SalvarConfiguracao(excluida);
        armazenamento.SalvarConfiguracao(mantida);
        checkpoints.Avancar(excluida.Id, excluida.Rotina, new Checkpoint(new DateTime(2026, 5, 1), 10));
        checkpoints.Avancar(mantida.Id, mantida.Rotina, new Checkpoint(new DateTime(2026, 6, 1), 20));

        armazenamento.ExcluirConfiguracao(excluida.Id);

        Assert.Equal([mantida], Novo().Carregar().Configuracoes);
        var relidos = new Checkpoints(_pasta.Caminho);
        Assert.Null(relidos.Obter(excluida.Id, excluida.Rotina));
        Assert.Equal(new Checkpoint(new DateTime(2026, 6, 1), 20), relidos.Obter(mantida.Id, mantida.Rotina));
    }

    [Fact]
    public void Gravacao_interrompida_nao_corrompe_o_arquivo_anterior()
    {
        var configuracao = Configuracao();
        ComInstancia().SalvarConfiguracao(configuracao);
        var caminho = _pasta.Arquivo(ArmazenamentoConfiguracao.NomeArquivo);
        var anterior = File.ReadAllText(caminho);

        Assert.Throws<IOException>(() => ArquivoAtomico.Gravar(caminho, arquivo =>
        {
            arquivo.Write("{\"instancias\":[{\"id\":"u8);
            throw new IOException("queda no meio da gravação");
        }));

        Assert.Equal(anterior, File.ReadAllText(caminho));
        Assert.Equal([configuracao], Novo().Carregar().Configuracoes);
        Assert.Equal([ArmazenamentoConfiguracao.NomeArquivo], Directory.GetFiles(_pasta.Caminho).Select(Path.GetFileName));
    }
}
