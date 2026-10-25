using System.Text.Json;
using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Modulos.Auditoria;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Exclusao;

/// <summary>
/// Volume: tenants com 12.000 linhas cada (10.000 EMPRESA + 1.000 USUARIO + 1.000 REFRESH_TOKEN), como no "Done when"
/// da T37, com lote de 400 linhas para forçar vários lotes por tabela (25 em EMPRESA, 3 em USUARIO e REFRESH_TOKEN)
/// e sem pausa entre lotes, para o teste ser rápido.
/// </summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ProcessadorExclusaoTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const int Lote = 400;
    private ServiceProvider _servicos = null!;

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _servicos = ServicosProcessador(banco);
        // Jobs deixados por outros testes da coleção não interferem nas contagens de cada teste.
        await ProcessarTudoAsync(Processador());
    }

    public async Task DisposeAsync() => await _servicos.DisposeAsync();

    /// <summary>
    /// Mesmos registros do Program.cs, sem pausa e com lote pequeno; <paramref name="tamanhoLote"/> nulo mantém o
    /// lote padrão das opções.
    /// </summary>
    internal static ServiceProvider ServicosProcessador(BancoDeTestes banco, int? tamanhoLote = Lote)
    {
        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        servicos.AddLogging();
        servicos.AddDadosPlataforma(banco.ConnectionString);
        servicos.AddAuditoria();
        servicos.AddExclusao();
        servicos.Configure<ExclusaoOptions>(o =>
        {
            if (tamanhoLote is { } lote) o.TamanhoLote = lote;
            o.PausaEntreLotes = TimeSpan.Zero;
        });
        return servicos.BuildServiceProvider();
    }

    private ProcessadorExclusao Processador() => ActivatorUtilities.CreateInstance<ProcessadorExclusao>(_servicos);

    internal static async Task ProcessarTudoAsync(ProcessadorExclusao processador)
    {
        while (await processador.ProcessarProximoAsync()) { }
    }

    private PlanoExclusao Plano => _servicos.GetRequiredService<PlanoExclusao>();

    /// <summary>Como a solicitação (T36) deixa: tenant inativo e job criado.</summary>
    internal static async Task<ExclusaoTenant> NovoJobAsync(BancoDeTestes banco, Tenant tenant, Guid solicitante,
        SituacaoExclusao situacao = SituacaoExclusao.Pendente, string? etapa = null, string? linhas = null,
        DateTime? heartbeat = null)
    {
        await using var contexto = banco.CriarContexto();
        var gravado = await contexto.Tenants.SingleAsync(t => t.Id == tenant.Id);
        gravado.Ativo = false;
        var job = new ExclusaoTenant
        {
            TenantId = tenant.Id,
            TenantIdPublico = tenant.IdPublico,
            TenantNome = tenant.Nome,
            Situacao = situacao,
            EtapaAtual = etapa,
            LinhasRemovidas = linhas,
            SolicitadoPorIdPublico = solicitante,
            HeartbeatEm = heartbeat,
        };
        contexto.ExclusoesTenant.Add(job);
        await contexto.SaveChangesAsync();
        return job;
    }

    private async Task<ExclusaoTenant> JobAsync(int id)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.ExclusoesTenant.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    private async Task<List<RegistroAuditoria>> AuditoriaAsync(Guid jobIdPublico)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Auditoria.AsNoTracking().Where(a => a.RecursoIdPublico == jobIdPublico)
            .OrderBy(a => a.Id).ToListAsync();
    }

    private static Dictionary<string, long> Linhas(ExclusaoTenant job) =>
        JsonSerializer.Deserialize<Dictionary<string, long>>(job.LinhasRemovidas!)!;

    /// <summary>
    /// Linhas removidas esperadas num job: toda tabela executada aparece, com 0 quando o tenant não tinha linhas nela;
    /// <paramref name="removidas"/> dá as contagens das demais. Assim tabelas novas do plano não quebram o teste.
    /// </summary>
    internal static Dictionary<string, long> LinhasEsperadas(IEnumerable<string> executadas, Dictionary<string, long> removidas)
    {
        var esperadas = executadas.ToDictionary(t => t, _ => 0L);
        foreach (var (tabela, linhas) in removidas) esperadas[tabela] = linhas;
        return esperadas;
    }

    [Fact]
    public async Task Exclui_todas_as_linhas_de_A_sem_alterar_B_e_conclui_o_job()
    {
        var a = await Dados.NovoTenantAsync(banco, "Cliente Exclusão A");
        var b = await Dados.NovoTenantAsync(banco, "Cliente Exclusão B");
        await DadosTenant.SemearAsync(banco, a.Id, empresas: 10_000, usuarios: 1_000);
        await DadosTenant.SemearAsync(banco, b.Id, empresas: 10_000, usuarios: 1_000);
        var superAdmin = await Dados.NovoUsuarioAsync(banco, Perfil.SuperAdmin);
        await banco.ExecutarAsync(
            "INSERT INTO REFRESH_TOKEN (USUARIO_ID, TENANT_ID, HASH, FAMILIA, EXPIRA_EM) " +
            $"VALUES ({superAdmin.Id}, NULL, HASHBYTES('SHA2_256', CONVERT(nvarchar(36), NEWID())), NEWID(), SYSUTCDATETIME())");
        var antesA = await DadosTenant.LinhasAsync(banco, a.Id);
        Assert.Equal(12_000, antesA.Where(p => p.Key != "TENANT").Sum(p => p.Value));
        var impressaoB = await DadosTenant.ImpressaoAsync(banco, b.Id);
        var job = await NovoJobAsync(banco, a, superAdmin.IdPublico);

        Assert.True(await Processador().ProcessarProximoAsync());

        Assert.All(await DadosTenant.LinhasAsync(banco, a.Id), p => Assert.Equal(0, p.Value));
        Assert.Equal(impressaoB, await DadosTenant.ImpressaoAsync(banco, b.Id));
        Assert.Equal(1, await banco.ConsultarEscalarAsync<int>(
            $"SELECT COUNT(*) FROM REFRESH_TOKEN WHERE USUARIO_ID = {superAdmin.Id} AND TENANT_ID IS NULL"));
        Assert.Equal(1, await banco.ConsultarEscalarAsync<int>($"SELECT COUNT(*) FROM USUARIO WHERE ID = {superAdmin.Id}"));

        var concluido = await JobAsync(job.Id);
        Assert.Equal(SituacaoExclusao.Concluida, concluido.Situacao);
        Assert.NotNull(concluido.ConcluidoEm);
        Assert.Equal("TENANT", concluido.EtapaAtual);
        Assert.Equal(
            LinhasEsperadas(Plano.Tabelas,
                new() { ["EMPRESA"] = 10_000, ["REFRESH_TOKEN"] = 1_000, ["USUARIO"] = 1_000, ["TENANT"] = 1 }),
            Linhas(concluido));

        var registros = await AuditoriaAsync(job.IdPublico);
        Assert.Equal(["EXCLUSAO_INICIADA", "EXCLUSAO_CONCLUIDA"], registros.Select(r => r.Operacao));
        Assert.All(registros, r =>
        {
            Assert.Equal(a.IdPublico, r.TenantIdPublico);
            Assert.Equal("Cliente Exclusão A", r.TenantNome);
            Assert.Null(r.UsuarioIdPublico); // autor: o sistema
            Assert.Contains($"\"solicitadoPorIdPublico\":\"{superAdmin.IdPublico}\"", r.Detalhes);
        });
        Assert.Contains("\"situacao\":\"EmExecucao\"", registros[0].Detalhes);
        Assert.Contains("\"situacao\":\"Concluida\"", registros[1].Detalhes);
    }

    /// <summary>
    /// Tabelas da sincronização de vendas no plano: VENDA, LOTE_INGESTAO, ORIGEM_SINCRONIZACAO (dona da EMPRESA) e
    /// COLETOR_TENANT saem com o tenant; o COLETOR, compartilhado com outro tenant, fica.
    /// </summary>
    [Fact]
    public async Task Exclui_vendas_lotes_e_origens_de_A_sem_alterar_B_nem_o_coletor()
    {
        var a = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Vendas A");
        var b = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Vendas B");
        var coletor = await Dados.NovoColetorAsync(banco);
        await DadosTenant.SemearSincronizacaoAsync(banco, a.Id, coletor.Id, vendas: 1_000, lotes: 3);
        await DadosTenant.SemearSincronizacaoAsync(banco, b.Id, coletor.Id, vendas: 1_000, lotes: 3);
        var impressaoB = await DadosTenant.ImpressaoAsync(banco, b.Id);
        var job = await NovoJobAsync(banco, a, Guid.NewGuid());

        Assert.True(await Processador().ProcessarProximoAsync());

        var concluido = await JobAsync(job.Id);
        Assert.Equal(SituacaoExclusao.Concluida, concluido.Situacao);
        Assert.Equal(
            LinhasEsperadas(Plano.Tabelas, new()
            {
                ["VENDA"] = 1_000, ["LOTE_INGESTAO"] = 3, ["EMPRESA"] = 1, ["ORIGEM_SINCRONIZACAO"] = 1,
                ["COLETOR_TENANT"] = 1, ["TENANT"] = 1,
            }),
            Linhas(concluido));
        Assert.All(await DadosTenant.LinhasAsync(banco, a.Id), p => Assert.Equal(0, p.Value));
        Assert.Equal(impressaoB, await DadosTenant.ImpressaoAsync(banco, b.Id));
        Assert.Equal(1, await banco.ConsultarEscalarAsync<int>($"SELECT COUNT(*) FROM COLETOR WHERE ID = {coletor.Id}"));
    }

    /// <summary>
    /// DEL-04 com o lote padrão (sem trocar o valor no teste): um trigger de teste em EMPRESA registra, a cada DELETE,
    /// a transação e quantas linhas do tenant saíram. 12.000 linhas → 5.000 + 5.000 + 2.000 + lote vazio que encerra
    /// a etapa, cada um na sua transação.
    /// </summary>
    [Fact]
    public async Task Lote_padrao_apaga_no_maximo_5000_linhas_por_transacao()
    {
        await using var servicos = ServicosProcessador(banco, tamanhoLote: null);
        var a = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Lote Padrão");
        await DadosTenant.SemearAsync(banco, a.Id, empresas: 12_000, usuarios: 1);
        var job = await NovoJobAsync(banco, a, Guid.NewGuid());
        await banco.ExecutarAsync($"""
            CREATE TABLE [TESTE_LOTE_EXCLUSAO] ([ORDEM] int IDENTITY PRIMARY KEY, [TRANSACAO] bigint NOT NULL,
                [LINHAS] int NOT NULL);
            EXEC ('CREATE TRIGGER [TR_TESTE_LOTE_EXCLUSAO] ON [EMPRESA] AFTER DELETE AS
                SET NOCOUNT ON; -- sem isto, o INSERT entra nas linhas afetadas do DELETE e o lote vazio não chega a 0
                INSERT INTO [TESTE_LOTE_EXCLUSAO] ([TRANSACAO], [LINHAS])
                SELECT CURRENT_TRANSACTION_ID(), COUNT(*) FROM deleted WHERE TENANT_ID = {a.Id}');
            """);
        List<(long Transacao, int Linhas)> lotes = [];
        try
        {
            Assert.True(await ActivatorUtilities.CreateInstance<ProcessadorExclusao>(servicos).ProcessarProximoAsync());

            await using var conexao = new SqlConnection(banco.ConnectionString);
            await conexao.OpenAsync();
            await using var comando = new SqlCommand(
                "SELECT [TRANSACAO], [LINHAS] FROM [TESTE_LOTE_EXCLUSAO] ORDER BY [ORDEM]", conexao);
            await using var leitor = await comando.ExecuteReaderAsync();
            while (await leitor.ReadAsync()) lotes.Add((leitor.GetInt64(0), leitor.GetInt32(1)));
        }
        finally
        {
            await banco.ExecutarAsync("DROP TRIGGER [TR_TESTE_LOTE_EXCLUSAO]; DROP TABLE [TESTE_LOTE_EXCLUSAO];");
        }

        Assert.Equal([5_000, 5_000, 2_000, 0], lotes.Select(l => l.Linhas));
        // Um DELETE por transação: nenhuma transação soma mais que o lote.
        Assert.Equal(lotes.Count, lotes.Select(l => l.Transacao).Distinct().Count());
        var concluido = await JobAsync(job.Id);
        Assert.Equal(SituacaoExclusao.Concluida, concluido.Situacao);
        Assert.Equal(12_000, Linhas(concluido)["EMPRESA"]);
        Assert.Equal(0, (await DadosTenant.LinhasAsync(banco, a.Id))["EMPRESA"]);
    }

    [Fact]
    public async Task Falha_na_segunda_tabela_marca_Falhou_com_a_etapa_e_mantem_o_tenant_inativo()
    {
        var a = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Falha");
        await DadosTenant.SemearAsync(banco, a.Id, empresas: 1_000, usuarios: 5);
        // As duas primeiras etapas com linhas do tenant (o trigger só dispara quando o DELETE apaga algo).
        var semeadas = new Dictionary<string, long> { ["EMPRESA"] = 1_000, ["REFRESH_TOKEN"] = 5, ["USUARIO"] = 5 };
        var comLinhas = Plano.Tabelas.Where(semeadas.ContainsKey).ToList();
        var primeira = comLinhas[0];
        var segunda = comLinhas[1];
        var job = await NovoJobAsync(banco, a, Guid.NewGuid());
        await banco.ExecutarAsync($"""
            CREATE TRIGGER [TR_TESTE_FALHA_EXCLUSAO] ON [{segunda}] AFTER DELETE AS
            IF EXISTS (SELECT 1 FROM deleted WHERE TENANT_ID = {a.Id}) THROW 50001, 'Falha injetada pelo teste', 1;
            """);
        try
        {
            Assert.True(await Processador().ProcessarProximoAsync());
        }
        finally
        {
            await banco.ExecutarAsync("DROP TRIGGER [TR_TESTE_FALHA_EXCLUSAO]");
        }

        var falhou = await JobAsync(job.Id);
        Assert.Equal(SituacaoExclusao.Falhou, falhou.Situacao);
        Assert.Equal(segunda, falhou.EtapaAtual);
        Assert.Contains("50001", falhou.Erro);
        Assert.Contains("Falha injetada pelo teste", falhou.Erro);
        Assert.DoesNotContain(" at ", falhou.Erro); // sem stack trace
        Assert.Null(falhou.ConcluidoEm);
        // A primeira etapa foi concluída; o lote da segunda foi desfeito.
        Assert.Equal(semeadas[primeira], Linhas(falhou)[primeira]);
        var linhas = await DadosTenant.LinhasAsync(banco, a.Id);
        Assert.Equal(0, linhas[primeira]);
        Assert.Equal(semeadas[segunda], linhas[segunda]);
        Assert.Equal(1, linhas["TENANT"]);
        Assert.False(await banco.ConsultarEscalarAsync<bool>($"SELECT ATIVO FROM TENANT WHERE ID = {a.Id}"));

        var registros = await AuditoriaAsync(job.IdPublico);
        Assert.Equal(["EXCLUSAO_INICIADA", "EXCLUSAO_FALHOU"], registros.Select(r => r.Operacao));
        Assert.Equal("Falha", registros[1].Resultado);
        Assert.Contains("\"situacao\":\"Falhou\"", registros[1].Detalhes);
    }

    [Fact]
    public async Task Job_EmExecucao_sem_heartbeat_e_retomado_da_etapa_salva()
    {
        var parado = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Parada");
        var ativo = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Em Andamento");
        // EMPRESA já foi apagada antes da parada (7 linhas, registradas no job).
        await DadosTenant.SemearAsync(banco, parado.Id, empresas: 0, usuarios: 3);
        var agora = DateTime.UtcNow;
        var semHeartbeat = await NovoJobAsync(banco, parado, Guid.NewGuid(), SituacaoExclusao.EmExecucao,
            etapa: Plano.Etapas[1].Tabela, linhas: "{\"EMPRESA\":7}", heartbeat: agora.AddMinutes(-3));
        var comHeartbeat = await NovoJobAsync(banco, ativo, Guid.NewGuid(), SituacaoExclusao.EmExecucao,
            etapa: Plano.Etapas[0].Tabela, heartbeat: agora);
        try
        {
            await ProcessarTudoAsync(Processador());

            var retomado = await JobAsync(semHeartbeat.Id);
            Assert.Equal(SituacaoExclusao.Concluida, retomado.Situacao);
            // Retomado da 2ª etapa: a 1ª não volta a ser executada.
            Assert.Equal(
                LinhasEsperadas(Plano.Tabelas.Skip(1),
                    new() { ["EMPRESA"] = 7, ["REFRESH_TOKEN"] = 3, ["USUARIO"] = 3, ["TENANT"] = 1 }),
                Linhas(retomado));
            Assert.All(await DadosTenant.LinhasAsync(banco, parado.Id), p => Assert.Equal(0, p.Value));
            // Job com heartbeat recente pertence a outro worker vivo: não é tocado.
            var intocado = await JobAsync(comHeartbeat.Id);
            Assert.Equal(SituacaoExclusao.EmExecucao, intocado.Situacao);
            Assert.Equal(Plano.Etapas[0].Tabela, intocado.EtapaAtual);
            Assert.Equal(1, await banco.ConsultarEscalarAsync<int>($"SELECT COUNT(*) FROM TENANT WHERE ID = {ativo.Id}"));
        }
        finally
        {
            // Não deixa um job reivindicável para os próximos testes da coleção.
            await banco.ExecutarAsync($"UPDATE EXCLUSAO_TENANT SET SITUACAO = 'Falhou' WHERE ID = {comHeartbeat.Id}");
        }
    }

    [Fact]
    public async Task Dois_processadores_simultaneos_nao_pegam_o_mesmo_job()
    {
        var x = await Dados.NovoTenantAsync(banco, "Cliente Exclusão X");
        var y = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Y");
        await DadosTenant.SemearAsync(banco, x.Id, empresas: 2_000, usuarios: 10);
        await DadosTenant.SemearAsync(banco, y.Id, empresas: 2_000, usuarios: 10);
        var jobX = await NovoJobAsync(banco, x, Guid.NewGuid());
        var jobY = await NovoJobAsync(banco, y, Guid.NewGuid());

        var resultados = await Task.WhenAll(
            Task.Run(() => Processador().ProcessarProximoAsync()),
            Task.Run(() => Processador().ProcessarProximoAsync()));

        Assert.Equal(new[] { true, true }, resultados);
        foreach (var job in new[] { jobX, jobY })
        {
            Assert.Equal(SituacaoExclusao.Concluida, (await JobAsync(job.Id)).Situacao);
            Assert.Single(await AuditoriaAsync(job.IdPublico), r => r.Operacao == "EXCLUSAO_INICIADA");
        }
    }

    [Fact]
    public async Task Worker_ativo_processa_job_pendente_sozinho()
    {
        var tenant = await Dados.NovoTenantAsync(banco, "Cliente Exclusão Worker");
        await DadosTenant.SemearAsync(banco, tenant.Id, empresas: 10, usuarios: 2);
        var job = await NovoJobAsync(banco, tenant, Guid.NewGuid());

        await using var api = new ApiFactory(banco).WithWebHostBuilder(b =>
        {
            b.UseSetting("Exclusao:WorkerAtivo", "true");
            b.UseSetting("Exclusao:Intervalo", "00:00:00.200");
            b.UseSetting("Exclusao:PausaEntreLotes", "00:00:00");
        });
        using var cliente = api.CreateClient(); // sobe o host e o worker

        var limite = DateTime.UtcNow.AddSeconds(30);
        while ((await JobAsync(job.Id)).Situacao != SituacaoExclusao.Concluida && DateTime.UtcNow < limite)
            await Task.Delay(100);

        Assert.Equal(SituacaoExclusao.Concluida, (await JobAsync(job.Id)).Situacao);
        Assert.All(await DadosTenant.LinhasAsync(banco, tenant.Id), p => Assert.Equal(0, p.Value));
    }
}
