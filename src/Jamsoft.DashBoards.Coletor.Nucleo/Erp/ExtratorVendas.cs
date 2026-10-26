using System.Data;
using Dapper;
using Jamsoft.DashBoards.Coletor.Nucleo.Logs;
using Jamsoft.DashBoards.Coletor.Nucleo.Sincronizacao;
using Jamsoft.DashBoards.Contratos.Ingestao;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Coletor.Nucleo.Erp;

/// <summary>
/// Uma venda lida do ERP. <see cref="DataFatura"/> (só data) alimenta o filtro e o checkpoint; <see cref="DataHora"/>
/// vem de <c>DataHoraFechamento</c> com o offset de America/Sao_Paulo. Quando o ERP não tem
/// <c>DataHoraFechamento</c>, <see cref="DataHora"/> é a <c>datafatura</c> às 00:00 e <see cref="SemDataHoraFechamento"/>
/// fica verdadeiro, para o executor registrar o aviso no log.
/// </summary>
public sealed record VendaErp(
    int VendaId,
    string TipoDfe,
    DateTimeOffset DataHora,
    bool SemDataHoraFechamento,
    DateTime DataFatura,
    decimal Valor,
    int? CodCliente,
    string? NomeCliente,
    int? VendedorCodigo,
    string? VendedorNome,
    string Situacao,
    string NomeEmpresa,
    int CodEmpresa,
    int QtdItens)
{
    public const int TamanhoNomeCliente = 120;
    public const int TamanhoVendedorNome = 80;
    public const int TamanhoNomeEmpresa = 80;

    /// <summary>Posição desta venda na ordem do keyset; vira o checkpoint depois do ACK (SYNC-03).</summary>
    public Checkpoint Posicao => new(DataFatura, VendaId);

    /// <summary>Aplica os tamanhos do contrato antes do envio (edge case: truncar, não rejeitar).</summary>
    public VendaContrato ParaContrato() => new(VendaId, TipoDfe, DataHora, Valor, CodCliente,
        Truncar(NomeCliente, TamanhoNomeCliente), VendedorCodigo, Truncar(VendedorNome, TamanhoVendedorNome), Situacao,
        Truncar(NomeEmpresa, TamanhoNomeEmpresa)!, CodEmpresa, QtdItens);

    private static string? Truncar(string? texto, int tamanho) => texto is { Length: var n } && n > tamanho ? texto[..tamanho] : texto;
}

/// <summary>Falha ao falar com o ERP, já classificada para o log (LOG-03): <see cref="TipoErro.Conexao"/> ou <see cref="TipoErro.Consulta"/>.</summary>
public sealed class ErroErpException(TipoErro tipo, string mensagem, Exception? interna = null) : Exception(mensagem, interna)
{
    public TipoErro Tipo { get; } = tipo;
}

/// <summary>
/// Lê as vendas do ERP com Dapper (SYNC-01, SYNC-02, SYNC-07, SYNC-08): a query do §11 com as regras confirmadas em
/// 2026-10-25, paginada por keyset (<c>datafatura</c>, <c>NumNotaSaida</c>), só com parâmetros, em
/// <c>READ COMMITTED</c> e com tempo limite por lote.
/// </summary>
public sealed class ExtratorVendas
{
    /// <summary>SYNC-08: nenhuma consulta ao ERP passa de 120 s.</summary>
    public static readonly TimeSpan TimeoutPadrao = TimeSpan.FromSeconds(120);

    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

    // A query do design ("ExtratorVendas (Dapper)"). Os apelidos seguem as propriedades de Linha para o Dapper.
    private const string SqlLote = """
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        SELECT TOP (@Tamanho)
            ns.NumNotaSaida                                    AS VendaId,
            CASE WHEN NS.TipoFatura = 2 THEN 'Pedido' ELSE CASE WHEN ns.modelo = 65 THEN 'NFC-e' ELSE 'NF-e' END END AS TipoDfe,
            ns.DataHoraFechamento                              AS DataHoraFechamento,
            ns.datafatura                                      AS DataFatura,
            ns.totnota                                         AS Valor,
            ns.codcliente                                      AS CodCliente,
            ns.FANTASIA                                        AS NomeCliente,
            ns.codvendedor                                     AS VendedorCodigo,
            ns.nomevend                                        AS VendedorNome,
            CASE ns.situacaonota
                WHEN 5  THEN 'Finalizada'
                WHEN 6  THEN 'Finalizada'
                WHEN 9  THEN 'Cancelada'
                WHEN 11 THEN 'Inutilizada'
                WHEN 12 THEN N'Contingência'
            END                                                AS Situacao,
            ns.Empresa                                         AS NomeEmpresa,
            ns.codEmpresa                                      AS CodEmpresa,
            (SELECT COUNT(*) FROM NotaSaidaProd
              WHERE NotaSaidaProd.numnotasaida = ns.numnotasaida) AS QtdItens
        FROM c_NotaSaida AS ns
        WHERE NS.TipoFatura<9 and ns.situacaonota IN (5, 6, 9, 11, 12)
          AND ns.TotalCondNeutra = 0
          AND ns.datafatura >= @Inicio AND ns.datafatura < @Fim
          AND (ns.datafatura > @CursorData
               OR (ns.datafatura = @CursorData AND ns.NumNotaSaida > @CursorNum))
        ORDER BY ns.datafatura, ns.NumNotaSaida;
        """;

    private readonly Func<CancellationToken, Task<SqlConnection>> _abrirConexao;
    private readonly int _timeoutSegundos;

    public ExtratorVendas(string connectionString, TimeSpan? timeoutConsulta = null)
        : this(ct => AbrirAsync(connectionString, ct), timeoutConsulta)
    {
    }

    /// <param name="abrirConexao">Devolve uma conexão aberta com o banco do ERP.</param>
    /// <param name="timeoutConsulta">Tempo limite de cada consulta; padrão <see cref="TimeoutPadrao"/>.</param>
    public ExtratorVendas(Func<CancellationToken, Task<SqlConnection>> abrirConexao, TimeSpan? timeoutConsulta = null)
    {
        _abrirConexao = abrirConexao;
        _timeoutSegundos = Math.Max(1, (int)Math.Ceiling((timeoutConsulta ?? TimeoutPadrao).TotalSeconds));
    }

    /// <summary>Hora do servidor do ERP: o limite superior da execução (edge case do relógio, SYNC-01).</summary>
    public Task<DateTimeOffset> AgoraDoServidorAsync(CancellationToken ct) =>
        ConsultarAsync(conexao => conexao.ExecuteScalarAsync<DateTimeOffset>(
            new CommandDefinition("SELECT SYSDATETIMEOFFSET();", commandTimeout: _timeoutSegundos, cancellationToken: ct)), ct);

    /// <summary>
    /// Próximas <paramref name="tamanho"/> vendas com <c>datafatura</c> em [<paramref name="inicio"/>,
    /// <paramref name="fimExclusivo"/>) depois de <paramref name="cursor"/>, em ordem (<c>datafatura</c>,
    /// <c>NumNotaSaida</c>). Sem cursor, começa em (<paramref name="inicio"/>, 0).
    /// </summary>
    /// <exception cref="ErroErpException">Sem conexão (Conexão) ou consulta com erro ou acima do tempo limite (Consulta).</exception>
    public Task<IReadOnlyList<VendaErp>> LerLoteAsync(DateTime inicio, DateTime fimExclusivo, Checkpoint? cursor,
        int tamanho = Limites.VendasPorLote, CancellationToken ct = default)
    {
        var parametros = new DynamicParameters();
        parametros.Add("Tamanho", tamanho, DbType.Int32);
        // DbType.DateTime casa com as colunas datetime do ERP e evita conversão implícita na coluna.
        parametros.Add("Inicio", inicio, DbType.DateTime);
        parametros.Add("Fim", fimExclusivo, DbType.DateTime);
        parametros.Add("CursorData", cursor?.DataFatura ?? inicio, DbType.DateTime);
        parametros.Add("CursorNum", cursor?.NumNotaSaida ?? 0, DbType.Int32);

        return ConsultarAsync<IReadOnlyList<VendaErp>>(async conexao =>
        {
            var linhas = await conexao.QueryAsync<Linha>(new CommandDefinition(
                SqlLote, parametros, commandTimeout: _timeoutSegundos, cancellationToken: ct));
            return [.. linhas.Select(Mapear)];
        }, ct);
    }

    private static VendaErp Mapear(Linha l)
    {
        var local = l.DataHoraFechamento ?? l.DataFatura.Date;
        var dataHora = new DateTimeOffset(local, SaoPaulo.GetUtcOffset(local));
        return new VendaErp(l.VendaId, l.TipoDfe, dataHora, l.DataHoraFechamento is null, l.DataFatura, l.Valor,
            l.CodCliente, l.NomeCliente, l.VendedorCodigo, l.VendedorNome, l.Situacao, l.NomeEmpresa, l.CodEmpresa, l.QtdItens);
    }

    private async Task<T> ConsultarAsync<T>(Func<SqlConnection, Task<T>> consulta, CancellationToken ct)
    {
        SqlConnection conexao;
        try
        {
            conexao = await _abrirConexao(ct);
        }
        catch (SqlException erro)
        {
            throw new ErroErpException(TipoErro.Conexao, $"Não foi possível conectar ao banco do ERP: {erro.Message}", erro);
        }

        await using (conexao)
        {
            try
            {
                return await consulta(conexao);
            }
            catch (SqlException erro) when (!ct.IsCancellationRequested)
            {
                var mensagem = erro.Number == -2
                    ? $"A consulta ao ERP passou de {_timeoutSegundos} s e foi cancelada."
                    : $"Erro na consulta ao ERP: {erro.Message}";
                throw new ErroErpException(TipoErro.Consulta, mensagem, erro);
            }
        }
    }

    private static async Task<SqlConnection> AbrirAsync(string connectionString, CancellationToken ct)
    {
        var conexao = new SqlConnection(connectionString);
        try
        {
            await conexao.OpenAsync(ct);
            return conexao;
        }
        catch
        {
            await conexao.DisposeAsync();
            throw;
        }
    }

    private sealed class Linha
    {
        public int VendaId { get; init; }
        public string TipoDfe { get; init; } = "";
        public DateTime? DataHoraFechamento { get; init; }
        public DateTime DataFatura { get; init; }
        public decimal Valor { get; init; }
        public int? CodCliente { get; init; }
        public string? NomeCliente { get; init; }
        public int? VendedorCodigo { get; init; }
        public string? VendedorNome { get; init; }
        public string Situacao { get; init; } = "";
        public string NomeEmpresa { get; init; } = "";
        public int CodEmpresa { get; init; }
        public int QtdItens { get; init; }
    }
}
