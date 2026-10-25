using Jamsoft.DashBoards.Api.Modulos.Empresas;
using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Api.Modulos.Tenants;

namespace Jamsoft.DashBoards.Api.Testes.Infraestrutura;

/// <summary>Tenant com duas empresas (Matriz e Filial) e a origem que recebe as vendas.</summary>
public sealed record ConjuntoVendas(Tenant Tenant, Empresa Matriz, Empresa Filial, int OrigemId);

/// <summary>
/// Conjunto fixo de vendas do painel, com os valores esperados calculados à mão. Datas em São Paulo (−03:00).
/// <para>Período <see cref="Inicio"/>..<see cref="Fim"/> (10 dias; anterior: 01/10..10/10):</para>
/// <code>
/// #  data/hora SP       empresa DF-e  valor   situação      itens vendedor   cliente
/// 1  11/10 10:00       Matriz  NF-e  100,00  Finalizada    2     1 Ana      10 Cliente X
/// 2  12/10 23:30       Matriz  NFC-e  50,00  Contingência  1     2 Bruno    -
/// 3  15/10 09:00       Matriz  NFC-e  30,00  Cancelada     3     1 Ana      10 Cliente X
/// 4  15/10 12:00       Matriz  NFC-e  20,00  Inutilizada   1     -          -
/// 5  15/10 15:00       Filial  NF-e  200,00  Finalizada    4     1 Ana      11 Cliente Y
/// 6  20/10 23:59       Filial  NFC-e  25,50  Finalizada    1     -          11 Cliente Y
/// 7  21/10 00:00       Matriz  NFC-e 1000,00 Finalizada    1     (fora: limite superior exclusivo)
/// 8  10/10 23:59       Matriz  NFC-e  40,00  Finalizada    2     (período anterior)
/// 9  05/10 12:00       Filial  NF-e   60,00  Finalizada    1     (período anterior)
/// 10 30/09 23:00       Matriz  NFC-e 500,00  Finalizada    1     (antes do período anterior)
/// </code>
/// Efetivadas no período (Finalizada + Contingência): 1, 2, 5, 6.
/// </summary>
public static class VendasSemeadas
{
    public static readonly DateOnly Inicio = new(2026, 10, 11);
    public static readonly DateOnly Fim = new(2026, 10, 20);
    public static readonly TimeSpan FusoSaoPaulo = TimeSpan.FromHours(-3);

    /// <summary>VENDA_ID das vendas de qualquer situação dentro do período (todas as empresas).</summary>
    public static readonly int[] IdsNoPeriodo = [1, 2, 3, 4, 5, 6];
    /// <summary>VENDA_ID das vendas da Filial dentro do período.</summary>
    public static readonly int[] IdsFilialNoPeriodo = [5, 6];

    // Valores esperados no período (efetivadas: 1, 2, 5, 6).
    public const decimal Faturamento = 375.50m;
    public const int Vendas = 4;
    public const decimal TicketMedio = 93.88m;       // 375,50 / 4 = 93,875
    public const decimal ItensPorVenda = 2.00m;      // (2 + 1 + 4 + 1) / 4
    public const decimal PercentualCanceladas = 20.00m; // 1 / (4 + 1) × 100

    // Período anterior (efetivadas: 8, 9).
    public const decimal FaturamentoAnterior = 100.00m;
    public const int VendasAnterior = 2;
    public const decimal TicketMedioAnterior = 50.00m;
    public const decimal ItensPorVendaAnterior = 1.50m;
    public const decimal PercentualCanceladasAnterior = 0m;

    public static DateTimeOffset Sp(int mes, int dia, int hora, int minuto) =>
        new(2026, mes, dia, hora, minuto, 0, FusoSaoPaulo);

    /// <summary>Grava o conjunto fixo no tenant.</summary>
    public static async Task<ConjuntoVendas> SemearAsync(BancoDeTestes banco, Tenant tenant)
    {
        var c = await PrepararAsync(banco, tenant);
        await InserirAsync(banco, c,
            Venda(c.Matriz, 1, Sp(10, 11, 10, 0), 100.00m, "Finalizada", "NF-e", 2, 10, "Cliente X", 1, "Ana"),
            Venda(c.Matriz, 2, Sp(10, 12, 23, 30), 50.00m, "Contingência", "NFC-e", 1, null, null, 2, "Bruno"),
            Venda(c.Matriz, 3, Sp(10, 15, 9, 0), 30.00m, "Cancelada", "NFC-e", 3, 10, "Cliente X", 1, "Ana"),
            Venda(c.Matriz, 4, Sp(10, 15, 12, 0), 20.00m, "Inutilizada", "NFC-e", 1),
            Venda(c.Filial, 5, Sp(10, 15, 15, 0), 200.00m, "Finalizada", "NF-e", 4, 11, "Cliente Y", 1, "Ana"),
            Venda(c.Filial, 6, Sp(10, 20, 23, 59), 25.50m, "Finalizada", "NFC-e", 1, 11, "Cliente Y"),
            Venda(c.Matriz, 7, Sp(10, 21, 0, 0), 1000.00m, "Finalizada", "NFC-e", 1),
            Venda(c.Matriz, 8, Sp(10, 10, 23, 59), 40.00m, "Finalizada", "NFC-e", 2),
            Venda(c.Filial, 9, Sp(10, 5, 12, 0), 60.00m, "Finalizada", "NF-e", 1),
            Venda(c.Matriz, 10, Sp(9, 30, 23, 0), 500.00m, "Finalizada", "NFC-e", 1));
        return c;
    }

    /// <summary>Grava, sem vendas, a origem e as empresas Matriz (código 1) e Filial (código 2) do tenant.</summary>
    public static async Task<ConjuntoVendas> PrepararAsync(BancoDeTestes banco, Tenant tenant)
    {
        var coletor = await Dados.NovoColetorAsync(banco);
        await using var contexto = banco.CriarContexto();
        var origem = new OrigemSincronizacao
        {
            TenantId = tenant.Id, ColetorId = coletor.Id, ConfiguracaoId = Guid.NewGuid(), Rotina = "Vendas",
        };
        contexto.OrigensSincronizacao.Add(origem);
        await contexto.SaveChangesAsync();
        var matriz = new Empresa { TenantId = tenant.Id, CodEmpresa = 1, Nome = "Matriz", OrigemId = origem.Id };
        var filial = new Empresa { TenantId = tenant.Id, CodEmpresa = 2, Nome = "Filial", OrigemId = origem.Id };
        contexto.Empresas.AddRange(matriz, filial);
        await contexto.SaveChangesAsync();
        return new ConjuntoVendas(tenant, matriz, filial, origem.Id);
    }

    /// <summary>Grava as vendas no tenant e na origem do conjunto.</summary>
    public static async Task InserirAsync(BancoDeTestes banco, ConjuntoVendas c, params Venda[] vendas)
    {
        await using var contexto = banco.CriarContexto();
        foreach (var venda in vendas)
        {
            venda.TenantId = c.Tenant.Id;
            venda.OrigemId = c.OrigemId;
        }
        contexto.Vendas.AddRange(vendas);
        await contexto.SaveChangesAsync();
    }

    public static Venda Venda(Empresa empresa, int vendaId, DateTimeOffset dataHora, decimal valor,
        string situacao = "Finalizada", string tipoDfe = "NFC-e", int qtdItens = 1, int? codCliente = null,
        string? nomeCliente = null, int? vendedorCodigo = null, string? vendedorNome = null) => new()
        {
            EmpresaId = empresa.Id,
            CodEmpresa = empresa.CodEmpresa,
            VendaId = vendaId,
            TipoDfe = tipoDfe,
            DataHora = dataHora,
            Valor = valor,
            Situacao = situacao,
            QtdItens = qtdItens,
            CodCliente = codCliente,
            NomeCliente = nomeCliente,
            VendedorCodigo = vendedorCodigo,
            VendedorNome = vendedorNome,
        };
}
