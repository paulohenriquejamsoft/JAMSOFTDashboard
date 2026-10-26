using System.Diagnostics;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Coletor.Testes.Integracao.Erp;

/// <summary><see cref="ExtratorVendas"/> contra o ERP falso: SYNC-01, SYNC-02, SYNC-07, SYNC-08 e as regras do §11.</summary>
[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class ExtratorVendasTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private static readonly DateTime Dia = new(2026, 3, 10);
    private static readonly TimeSpan OffsetSaoPaulo = TimeSpan.FromHours(-3);

    private ErpFalso _erp = null!;

    public async Task InitializeAsync() => _erp = await ErpFalso.CriarAsync(banco);

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<IReadOnlyList<VendaErp>> LerTudoDoDiaAsync() => _erp.Extrator().LerLoteAsync(Dia, Dia.AddDays(1), cursor: null);

    [Fact]
    public async Task So_situacoes_5_6_9_11_12_voltam_com_o_texto_do_contrato()
    {
        var numeros = new Dictionary<int, int>();
        foreach (var situacao in Enumerable.Range(0, 14))
        {
            numeros[situacao] = await _erp.InserirAsync(new NotaFalsa(Dia, SituacaoNota: situacao));
        }

        var vendas = await LerTudoDoDiaAsync();

        Assert.Equal(
            [
                (numeros[5], "Finalizada"), (numeros[6], "Finalizada"), (numeros[9], "Cancelada"),
                (numeros[11], "Inutilizada"), (numeros[12], "Contingência"),
            ],
            vendas.Select(v => (v.VendaId, v.Situacao)));
    }

    [Fact]
    public async Task Venda_com_valor_neutro_fica_fora()
    {
        var semNeutro = await _erp.InserirAsync(new NotaFalsa(Dia, TotalCondNeutra: 0m));
        await _erp.InserirAsync(new NotaFalsa(Dia, TotalCondNeutra: 5m));

        var vendas = await LerTudoDoDiaAsync();

        Assert.Equal([semNeutro], vendas.Select(v => v.VendaId));
    }

    [Fact]
    public async Task Data_hora_vem_de_DataHoraFechamento_com_offset_de_Sao_Paulo()
    {
        var numero = await _erp.InserirAsync(new NotaFalsa(Dia, HoraFechamento: new TimeSpan(14, 35, 20), Modelo: 55, TotNota: 123.45m));

        var venda = Assert.Single(await LerTudoDoDiaAsync());

        Assert.Equal(numero, venda.VendaId);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 14, 35, 20, OffsetSaoPaulo), venda.DataHora);
        Assert.Equal(Dia, venda.DataFatura);
        Assert.False(venda.SemDataHoraFechamento);
        Assert.Equal(("NF-e", 123.45m, 10, "Cliente", 3, "Vendedor", "Loja 1", 1),
            (venda.TipoDfe, venda.Valor, venda.CodCliente, venda.NomeCliente, venda.VendedorCodigo, venda.VendedorNome,
             venda.NomeEmpresa, venda.CodEmpresa));
    }

    [Fact]
    public async Task Tipo_vem_de_TipoFatura_e_modelo_e_TipoFatura_9_ou_mais_fica_fora()
    {
        var nfce = await _erp.InserirAsync(new NotaFalsa(Dia, Modelo: 65));
        var nfe = await _erp.InserirAsync(new NotaFalsa(Dia, Modelo: 55));
        var pedido = await _erp.InserirAsync(new NotaFalsa(Dia, Modelo: 65, TipoFatura: 2));
        await _erp.InserirAsync(new NotaFalsa(Dia, TipoFatura: 9));

        var vendas = await LerTudoDoDiaAsync();

        Assert.Equal([(nfce, "NFC-e"), (nfe, "NF-e"), (pedido, "Pedido")], vendas.Select(v => (v.VendaId, v.TipoDfe)));
    }

    [Fact]
    public async Task DataHoraFechamento_nula_usa_a_datafatura_as_00h00_e_sinaliza_o_aviso()
    {
        await _erp.InserirAsync(new NotaFalsa(Dia, SemFechamento: true));

        var venda = Assert.Single(await LerTudoDoDiaAsync());

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 0, 0, 0, OffsetSaoPaulo), venda.DataHora);
        Assert.True(venda.SemDataHoraFechamento);
    }

    [Fact]
    public async Task Qtd_itens_conta_as_linhas_em_NotaSaidaProd()
    {
        var tres = await _erp.InserirAsync(new NotaFalsa(Dia, Itens: 3));
        var nenhum = await _erp.InserirAsync(new NotaFalsa(Dia, Itens: 0));

        var vendas = await LerTudoDoDiaAsync();

        Assert.Equal([(tres, 3), (nenhum, 0)], vendas.Select(v => (v.VendaId, v.QtdItens)));
    }

    [Fact]
    public async Task Mil_e_duzentas_vendas_saem_em_lotes_de_500_500_200_na_ordem_do_keyset_sem_repeticao()
    {
        var inicio = new DateTime(2026, 1, 1);
        await _erp.SemearAsync(1200, inicio, dias: 180);
        var esperado = (await ListarOrdemEsperadaAsync()).ToList();
        var extrator = _erp.Extrator();

        var tamanhos = new List<int>();
        var lidas = new List<int>();
        Checkpoint? cursor = null;
        while (true)
        {
            var lote = await extrator.LerLoteAsync(inicio, inicio.AddDays(180), cursor);
            if (lote.Count == 0)
            {
                break;
            }

            tamanhos.Add(lote.Count);
            lidas.AddRange(lote.Select(v => v.VendaId));
            cursor = lote[^1].Posicao;
        }

        Assert.Equal([500, 500, 200], tamanhos);
        Assert.Equal(1200, lidas.Distinct().Count());
        Assert.Equal(esperado, lidas);
    }

    [Fact]
    public async Task Inicio_e_inclusivo_e_limite_superior_e_exclusivo()
    {
        await _erp.InserirAsync(new NotaFalsa(Dia.AddDays(-1)));
        var noInicio = await _erp.InserirAsync(new NotaFalsa(Dia));
        var antesDoFim = await _erp.InserirAsync(new NotaFalsa(Dia.AddDays(1)));
        await _erp.InserirAsync(new NotaFalsa(Dia.AddDays(2)));

        var vendas = await _erp.Extrator().LerLoteAsync(Dia, Dia.AddDays(2), cursor: null);

        Assert.Equal([noInicio, antesDoFim], vendas.Select(v => v.VendaId));
    }

    [Fact]
    public async Task Consulta_acima_do_tempo_limite_e_cancelada_com_erro_de_Consulta()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), ExtratorVendas.TimeoutPadrao);
        await _erp.InserirAsync(new NotaFalsa(Dia));

        // Outra conexão segura um lock exclusivo na tabela base: a leitura em READ COMMITTED fica esperando.
        await using var bloqueio = new SqlConnection(_erp.ConnectionString);
        await bloqueio.OpenAsync();
        await using var transacao = (SqlTransaction)await bloqueio.BeginTransactionAsync();
        await using (var comando = new SqlCommand("UPDATE dbo.NotaSaida SET totnota = totnota + 1;", bloqueio, transacao))
        {
            await comando.ExecuteNonQueryAsync();
        }

        var cronometro = Stopwatch.StartNew();
        var erro = await Assert.ThrowsAsync<ErroErpException>(() =>
            _erp.Extrator(timeout: TimeSpan.FromSeconds(1)).LerLoteAsync(Dia, Dia.AddDays(1), cursor: null));
        cronometro.Stop();

        Assert.Equal(TipoErro.Consulta, erro.Tipo);
        Assert.Equal(-2, Assert.IsType<SqlException>(erro.InnerException).Number);
        Assert.InRange(cronometro.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        await transacao.RollbackAsync();
    }

    private async Task<IEnumerable<int>> ListarOrdemEsperadaAsync()
    {
        await using var conexao = new SqlConnection(_erp.ConnectionString);
        return await Dapper.SqlMapper.QueryAsync<int>(conexao,
            "SELECT NumNotaSaida FROM dbo.NotaSaida ORDER BY datafatura, NumNotaSaida;");
    }
}
