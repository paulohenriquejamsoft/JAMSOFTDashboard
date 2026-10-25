using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Modulos.Exclusao;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Exclusao;

/// <summary>DEL-04: lotes de no máximo 5.000 linhas por transação.</summary>
[Trait("Categoria", "Unidade")]
public sealed class ExclusaoOptionsTestes
{
    private static ExclusaoOptions Resolver(string? tamanhoLote)
    {
        var valores = new Dictionary<string, string?>();
        if (tamanhoLote is not null) valores["Exclusao:TamanhoLote"] = tamanhoLote;
        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());
        servicos.AddLogging();
        servicos.AddExclusao();
        using var provedor = servicos.BuildServiceProvider();
        return provedor.GetRequiredService<IOptions<ExclusaoOptions>>().Value;
    }

    [Fact]
    public void Lote_padrao_e_o_maximo_sao_5000()
    {
        Assert.Equal(5000, ExclusaoOptions.LoteMaximo);
        Assert.Equal(5000, new ExclusaoOptions().TamanhoLote);
        Assert.Equal(5000, Resolver(null).TamanhoLote);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5000)]
    public void Lote_entre_1_e_5000_e_valido(int lote)
    {
        Assert.True(new ExclusaoOptions { TamanhoLote = lote }.Valida());
        Assert.Equal(lote, Resolver(lote.ToString()).TamanhoLote);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    [InlineData(50000)]
    public void Lote_fora_de_1_a_5000_falha_na_validacao(int lote)
    {
        Assert.False(new ExclusaoOptions { TamanhoLote = lote }.Valida());
        Assert.Throws<OptionsValidationException>(() => Resolver(lote.ToString()));
    }
}
