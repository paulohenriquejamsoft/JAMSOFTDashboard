using Jamsoft.DashBoards.Api.Modulos.Ingestao;
using Jamsoft.DashBoards.Contratos.Ingestao;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Ingestao;

/// <summary>Contrato da venda (spec, "Modelo de dados da venda") e ING-06.</summary>
[Trait("Categoria", "Unidade")]
public sealed class ValidacaoVendaTestes
{
    private static VendaContrato Valida(int vendaId = 1, int codEmpresa = 1) => new(
        VendaId: vendaId, TipoDfe: "NFC-e", DataHora: new DateTimeOffset(2026, 10, 20, 14, 3, 0, TimeSpan.FromHours(-3)),
        Valor: 123.45m, CodCliente: 10, NomeCliente: "Cliente", VendedorCodigo: 3, VendedorNome: "Vendedor",
        Situacao: "Finalizada", NomeEmpresa: "Loja Centro", CodEmpresa: codEmpresa, QtdItens: 4);

    private static VendaRejeitada UnicaRejeitada(VendaContrato venda)
    {
        var resultado = ValidacaoVenda.Validar([venda]);
        Assert.Empty(resultado.Validas);
        return Assert.Single(resultado.Rejeitadas);
    }

    [Fact]
    public void Venda_valida_passa_intacta()
    {
        var venda = Valida();

        var resultado = ValidacaoVenda.Validar([venda]);

        Assert.Equal(venda, Assert.Single(resultado.Validas));
        Assert.Empty(resultado.Rejeitadas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void VendaId_nao_positivo_e_rejeitado(int vendaId)
    {
        var rejeitada = UnicaRejeitada(Valida(vendaId: vendaId));

        Assert.Equal(new VendaRejeitada(vendaId, 1, "vendaId deve ser maior que zero"), rejeitada);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CodEmpresa_nao_positivo_e_rejeitado(int codEmpresa)
    {
        var rejeitada = UnicaRejeitada(Valida(codEmpresa: codEmpresa));

        Assert.Equal(new VendaRejeitada(1, codEmpresa, "codEmpresa deve ser maior que zero"), rejeitada);
    }

    [Fact]
    public void Valor_negativo_e_rejeitado_e_zero_e_aceito()
    {
        Assert.Equal("valor não pode ser negativo", UnicaRejeitada(Valida() with { Valor = -0.01m }).Motivo);
        Assert.Single(ValidacaoVenda.Validar([Valida() with { Valor = 0m }]).Validas);
    }

    [Fact]
    public void QtdItens_negativo_e_rejeitado_e_zero_e_aceito()
    {
        Assert.Equal("qtdItens não pode ser negativo", UnicaRejeitada(Valida() with { QtdItens = -1 }).Motivo);
        Assert.Single(ValidacaoVenda.Validar([Valida() with { QtdItens = 0 }]).Validas);
    }

    [Fact]
    public void DataHora_ausente_e_rejeitada()
    {
        Assert.Equal("dataHora é obrigatória", UnicaRejeitada(Valida() with { DataHora = default }).Motivo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NomeEmpresa_ausente_e_rejeitado(string? nome)
    {
        Assert.Equal("nomeEmpresa é obrigatório", UnicaRejeitada(Valida() with { NomeEmpresa = nome! }).Motivo);
    }

    [Theory]
    [InlineData("Finalizada")]
    [InlineData("Cancelada")]
    [InlineData("Inutilizada")]
    [InlineData("Contingência")]
    public void As_quatro_situacoes_do_contrato_sao_aceitas(string situacao)
    {
        var resultado = ValidacaoVenda.Validar([Valida() with { Situacao = situacao }]);

        Assert.Equal(situacao, Assert.Single(resultado.Validas).Situacao);
        Assert.Empty(resultado.Rejeitadas);
    }

    [Theory]
    [InlineData("Aberta")]
    [InlineData("Contingencia")]
    [InlineData("finalizada")]
    [InlineData(null)]
    public void Situacao_fora_do_dominio_e_rejeitada(string? situacao)
    {
        Assert.Equal("situacao deve ser Finalizada, Cancelada, Inutilizada, Contingência",
            UnicaRejeitada(Valida() with { Situacao = situacao! }).Motivo);
    }

    [Theory]
    [InlineData("NF-e")]
    [InlineData("NFC-e")]
    public void Os_dois_tipos_de_DFe_sao_aceitos(string tipo)
    {
        Assert.Equal(tipo, Assert.Single(ValidacaoVenda.Validar([Valida() with { TipoDfe = tipo }]).Validas).TipoDfe);
    }

    [Theory]
    [InlineData("NFS-e")]
    [InlineData("nfc-e")]
    [InlineData(null)]
    public void TipoDfe_fora_do_dominio_e_rejeitado(string? tipo)
    {
        Assert.Equal("tipoDfe deve ser NF-e ou NFC-e", UnicaRejeitada(Valida() with { TipoDfe = tipo! }).Motivo);
    }

    [Fact]
    public void Varias_violacoes_na_mesma_venda_aparecem_todas_no_motivo()
    {
        var rejeitada = UnicaRejeitada(Valida() with { Valor = -1m, Situacao = "Aberta" });

        Assert.Equal("valor não pode ser negativo; situacao deve ser Finalizada, Cancelada, Inutilizada, Contingência",
            rejeitada.Motivo);
    }

    [Fact]
    public void Nomes_longos_sao_truncados_nos_tamanhos_do_contrato()
    {
        var venda = Valida() with
        {
            NomeCliente = new string('c', 130),
            VendedorNome = new string('v', 90),
            NomeEmpresa = new string('e', 85),
        };

        var resultado = ValidacaoVenda.Validar([venda]);

        var gravada = Assert.Single(resultado.Validas);
        Assert.Empty(resultado.Rejeitadas);
        Assert.Equal(new string('c', 120), gravada.NomeCliente);
        Assert.Equal(new string('v', 80), gravada.VendedorNome);
        Assert.Equal(new string('e', 80), gravada.NomeEmpresa);
        Assert.Equal(venda with
        {
            NomeCliente = gravada.NomeCliente,
            VendedorNome = gravada.VendedorNome,
            NomeEmpresa = gravada.NomeEmpresa
        }, gravada);
    }

    [Fact]
    public void Lote_misto_separa_validas_e_rejeitadas_com_vendaId_e_codEmpresa()
    {
        var vendas = Enumerable.Range(1, 10).Select(i => Valida(vendaId: i)).ToList();
        vendas[4] = vendas[4] with { Situacao = "Aberta" };

        var resultado = ValidacaoVenda.Validar(vendas);

        Assert.Equal([1, 2, 3, 4, 6, 7, 8, 9, 10], resultado.Validas.Select(v => v.VendaId));
        var rejeitada = Assert.Single(resultado.Rejeitadas);
        Assert.Equal((5, 1), (rejeitada.VendaId, rejeitada.CodEmpresa));
    }

    [Fact]
    public void Venda_repetida_no_lote_vale_a_primeira_e_a_segunda_e_rejeitada()
    {
        var primeira = Valida(vendaId: 7);
        var repetida = primeira with { Situacao = "Cancelada" };
        // Mesmo vendaId em outra empresa é outra venda.
        var outraEmpresa = Valida(vendaId: 7, codEmpresa: 2);

        var resultado = ValidacaoVenda.Validar([primeira, repetida, outraEmpresa]);

        Assert.Equal([primeira, outraEmpresa], resultado.Validas);
        Assert.Equal(new VendaRejeitada(7, 1, "Venda repetida no lote"), Assert.Single(resultado.Rejeitadas));
    }

    [Fact]
    public void Venda_nula_no_lote_e_rejeitada()
    {
        var resultado = ValidacaoVenda.Validar([null, Valida()]);

        Assert.Single(resultado.Validas);
        Assert.Equal(new VendaRejeitada(0, 0, "Venda ausente"), Assert.Single(resultado.Rejeitadas));
    }
}
