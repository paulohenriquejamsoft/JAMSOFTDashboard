using System.Runtime.CompilerServices;
using Dapper;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Jamsoft.DashBoards.Coletor.Nucleo.Erp;
using Microsoft.Data.SqlClient;

namespace Jamsoft.DashBoards.Coletor.Testes.Infraestrutura;

/// <summary>Nota de saída a gravar no ERP falso. <see cref="HoraFechamento"/> nula grava <c>DataHoraFechamento</c> nula.</summary>
public sealed record NotaFalsa(
    DateTime DataFatura,
    int SituacaoNota = 5,
    decimal TotalCondNeutra = 0m,
    TimeSpan? HoraFechamento = null,
    bool SemFechamento = false,
    int Modelo = 65,
    decimal TotNota = 10m,
    int? CodCliente = 10,
    string? Fantasia = "Cliente",
    int? CodVendedor = 3,
    string? NomeVendedor = "Vendedor",
    int CodEmpresa = 1,
    string Empresa = "Loja 1",
    int Itens = 2);

/// <summary>
/// Banco de ERP descartável com a estrutura confirmada em 2026-10-25: tabela <c>NotaSaida</c> com identity em
/// <c>NumNotaSaida</c>, view <c>c_NotaSaida</c> sobre ela e <c>NotaSaidaProd</c>. Um banco por coleção, criado no
/// servidor do <see cref="BancoDeTestes"/> com <see cref="BancoDeTestes.CriarBancoVazioAsync"/> (removido no fim da
/// coleção); cada <see cref="CriarAsync"/> recria as tabelas vazias. Os testes da coleção rodam em sequência.
/// </summary>
public sealed class ErpFalso
{
    private static readonly ConditionalWeakTable<BancoDeTestes, Task<string>> Bancos = new();

    private const string Limpar = """
        DROP VIEW IF EXISTS dbo.c_NotaSaida;
        DROP TABLE IF EXISTS dbo.NotaSaidaProd;
        DROP TABLE IF EXISTS dbo.NotaSaida;
        """;

    private const string Esquema = """
        CREATE TABLE dbo.NotaSaida (
            NumNotaSaida       int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
            modelo             int           NOT NULL,
            datafatura         datetime      NOT NULL,
            DataHoraFechamento datetime      NULL,
            dataentrega        datetime      NULL,
            totnota            money         NOT NULL,
            codcliente         int           NULL,
            FANTASIA           varchar(150)  NULL,
            codvendedor        int           NULL,
            nomevend           varchar(100)  NULL,
            situacaonota       int           NOT NULL,
            TotalCondNeutra    money         NOT NULL,
            Empresa            varchar(100)  NOT NULL,
            codEmpresa         int           NOT NULL);
        CREATE TABLE dbo.NotaSaidaProd (
            Id           int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
            numnotasaida int            NOT NULL,
            codproduto   int            NOT NULL,
            quantidade   decimal(18, 3) NOT NULL);
        """;

    private const string View = """
        CREATE VIEW dbo.c_NotaSaida AS
        SELECT NumNotaSaida, modelo, datafatura, DataHoraFechamento, dataentrega, totnota, codcliente, FANTASIA,
               codvendedor, nomevend, situacaonota, TotalCondNeutra, Empresa, codEmpresa
          FROM dbo.NotaSaida;
        """;

    private ErpFalso(string connectionString)
    {
        ConnectionString = connectionString;
        NomeBanco = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }

    public string NomeBanco { get; }

    /// <summary>Conexão do dono do banco.</summary>
    public string ConnectionString { get; }

    /// <summary>ERP falso da coleção, com as tabelas recriadas vazias (identity de volta a 1).</summary>
    public static async Task<ErpFalso> CriarAsync(BancoDeTestes banco)
    {
        var erp = new ErpFalso(await Bancos.GetValue(banco, b => b.CriarBancoVazioAsync()));
        await erp.ExecutarAsync(Limpar + Esquema);
        await erp.ExecutarAsync(View);
        return erp;
    }

    public ExtratorVendas Extrator(TimeSpan? timeout = null) => new(ConnectionString, timeout);

    public Task ExecutarAsync(string sql, object? parametros = null) => ExecutarAsync(ConnectionString, sql, parametros);

    public async Task<T> ConsultarAsync<T>(string sql, object? parametros = null)
    {
        await using var conexao = new SqlConnection(ConnectionString);
        return await conexao.ExecuteScalarAsync<T>(new CommandDefinition(sql, parametros, commandTimeout: 120))
            ?? throw new InvalidOperationException("Consulta sem resultado.");
    }

    /// <summary>Grava a nota e os itens; devolve o <c>NumNotaSaida</c> gerado pela identity.</summary>
    public async Task<int> InserirAsync(NotaFalsa nota)
    {
        await using var conexao = new SqlConnection(ConnectionString);
        var fechamento = nota.SemFechamento ? (DateTime?)null : nota.DataFatura.Date + (nota.HoraFechamento ?? TimeSpan.FromHours(10));
        var numero = await conexao.ExecuteScalarAsync<int>("""
            INSERT dbo.NotaSaida (modelo, datafatura, DataHoraFechamento, totnota, codcliente, FANTASIA, codvendedor, nomevend,
                                  situacaonota, TotalCondNeutra, Empresa, codEmpresa)
            VALUES (@Modelo, @DataFatura, @Fechamento, @TotNota, @CodCliente, @Fantasia, @CodVendedor, @NomeVendedor,
                    @SituacaoNota, @TotalCondNeutra, @Empresa, @CodEmpresa);
            DECLARE @Num int = SCOPE_IDENTITY();
            INSERT dbo.NotaSaidaProd (numnotasaida, codproduto, quantidade)
            SELECT TOP (@Itens) @Num, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 1 FROM sys.all_objects;
            SELECT @Num;
            """, new
        {
            nota.Modelo,
            DataFatura = nota.DataFatura.Date,
            Fechamento = fechamento,
            nota.TotNota,
            nota.CodCliente,
            nota.Fantasia,
            nota.CodVendedor,
            nota.NomeVendedor,
            nota.SituacaoNota,
            nota.TotalCondNeutra,
            nota.Empresa,
            nota.CodEmpresa,
            nota.Itens,
        });
        return numero;
    }

    /// <summary>
    /// Grava <paramref name="quantidade"/> notas finalizadas com <c>datafatura</c> espalhada por
    /// [<paramref name="de"/>, <paramref name="de"/> + <paramref name="dias"/>). A data não acompanha a identity
    /// (notas retroativas), para a ordem por (<c>datafatura</c>, <c>NumNotaSaida</c>) importar.
    /// </summary>
    public Task SemearAsync(int quantidade, DateTime de, int dias, int codEmpresa = 1) => ExecutarAsync("""
        WITH n AS (SELECT TOP (@Quantidade) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
                     FROM sys.all_objects a CROSS JOIN sys.all_objects b)
        INSERT dbo.NotaSaida (modelo, datafatura, DataHoraFechamento, totnota, codcliente, FANTASIA, codvendedor, nomevend,
                              situacaonota, TotalCondNeutra, Empresa, codEmpresa)
        SELECT 65, d.dia, DATEADD(minute, 600 + i % 600, d.dia), 10 + i % 90, 10, 'Cliente', 3, 'Vendedor', 5, 0,
               'Loja ' + CAST(@CodEmpresa AS varchar(10)), @CodEmpresa
          FROM n CROSS APPLY (SELECT DATEADD(day, CAST((i * 7919) % @Quantidade AS bigint) * @Dias / @Quantidade, @De) AS dia) d
         ORDER BY i;
        """, new { Quantidade = quantidade, De = de.Date, Dias = dias, CodEmpresa = codEmpresa });

    private static async Task ExecutarAsync(string conexaoTexto, string sql, object? parametros = null)
    {
        await using var conexao = new SqlConnection(conexaoTexto);
        await conexao.ExecuteAsync(new CommandDefinition(sql, parametros, commandTimeout: 120));
    }
}
