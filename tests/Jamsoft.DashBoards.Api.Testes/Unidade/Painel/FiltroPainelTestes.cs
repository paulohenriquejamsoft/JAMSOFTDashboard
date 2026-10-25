using Jamsoft.DashBoards.Api.Modulos.Painel;
using Microsoft.Extensions.Time.Testing;

namespace Jamsoft.DashBoards.Api.Testes.Unidade.Painel;

/// <summary>Filtro do painel (DASH-01, DASH-05, DASH-09, TRD-01..03).</summary>
[Trait("Categoria", "Unidade")]
public sealed class FiltroPainelTestes
{
    private static FiltroPainel Criar(DateOnly? inicio, DateOnly? fim, DateOnly? hoje = null)
    {
        Assert.True(FiltroPainel.TentarCriar(inicio, fim, null, hoje ?? new DateOnly(2026, 10, 24), out var filtro, out var erros),
            string.Join("; ", erros.SelectMany(e => e.Value)));
        Assert.Empty(erros);
        return filtro;
    }

    private static Dictionary<string, string[]> Erros(DateOnly inicio, DateOnly fim)
    {
        Assert.False(FiltroPainel.TentarCriar(inicio, fim, null, new DateOnly(2026, 10, 24), out _, out var erros));
        return erros;
    }

    [Fact]
    public void Sem_datas_o_periodo_e_do_dia_1_do_mes_corrente_ate_hoje_em_Sao_Paulo()
    {
        var hoje = FiltroPainel.HojeEmSaoPaulo(new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 15, 0, 0, TimeSpan.Zero)));

        var filtro = Criar(null, null, hoje);

        Assert.Equal(new DateOnly(2026, 10, 1), filtro.Inicio);
        Assert.Equal(new DateOnly(2026, 10, 24), filtro.Fim);
    }

    [Fact]
    public void Na_virada_do_mes_em_UTC_o_mes_corrente_ainda_e_o_de_Sao_Paulo()
    {
        // 01/11 02:00 UTC = 31/10 23:00 em São Paulo.
        var hoje = FiltroPainel.HojeEmSaoPaulo(new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero)));

        var filtro = Criar(null, null, hoje);

        Assert.Equal(new DateOnly(2026, 10, 31), hoje);
        Assert.Equal(new DateOnly(2026, 10, 1), filtro.Inicio);
        Assert.Equal(new DateOnly(2026, 10, 31), filtro.Fim);
    }

    [Fact]
    public void Faixa_UTC_vai_da_meia_noite_do_inicio_a_meia_noite_do_dia_seguinte_ao_fim_em_Sao_Paulo()
    {
        var filtro = Criar(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero), filtro.DesdeUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 3, 0, 0, TimeSpan.Zero), filtro.AteUtcExclusivo);
        Assert.Equal(TimeSpan.Zero, filtro.DesdeUtc.Offset);
        Assert.Equal(10, filtro.Dias);
    }

    [Fact]
    public void Fim_antes_do_inicio_da_erro_em_fim()
    {
        var erros = Erros(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 9));

        Assert.Equal(["fim"], erros.Keys);
    }

    [Fact]
    public void Exatamente_24_meses_e_aceito()
    {
        var filtro = Criar(new DateOnly(2024, 11, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(new DateOnly(2026, 10, 31), filtro.Fim);
    }

    [Fact]
    public void Um_dia_alem_de_24_meses_da_erro_em_fim()
    {
        var erros = Erros(new DateOnly(2024, 11, 1), new DateOnly(2026, 11, 1));

        Assert.Equal(["fim"], erros.Keys);
    }

    [Fact]
    public void Periodo_anterior_de_10_dias_termina_na_vespera_do_inicio()
    {
        var filtro = Criar(new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 20));

        Assert.Equal(new DateOnly(2026, 10, 1), filtro.Anterior.Inicio);
        Assert.Equal(new DateOnly(2026, 10, 10), filtro.Anterior.Fim);
        Assert.Equal(10, filtro.Anterior.Dias);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero), filtro.Anterior.DesdeUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 3, 0, 0, TimeSpan.Zero), filtro.Anterior.AteUtcExclusivo);
    }

    [Theory]
    [InlineData(62, Granularidade.Dia)]
    [InlineData(63, Granularidade.Mes)]
    public void Ate_62_dias_a_granularidade_e_diaria_e_acima_mensal(int dias, Granularidade esperada)
    {
        var inicio = new DateOnly(2026, 1, 1);

        var filtro = Criar(inicio, inicio.AddDays(dias - 1));

        Assert.Equal(dias, filtro.Dias);
        Assert.Equal(esperada, filtro.Granularidade);
    }

    [Fact]
    public void Buckets_diarios_tem_todos_os_dias_do_periodo()
    {
        var filtro = Criar(new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 3));

        Assert.Equal(
            [
                new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 31),
                new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3),
            ],
            filtro.Buckets());
    }

    [Fact]
    public void Buckets_mensais_tem_o_primeiro_dia_de_cada_mes_tocado_pelo_periodo()
    {
        var filtro = Criar(new DateOnly(2025, 11, 15), new DateOnly(2026, 3, 14));

        Assert.Equal(Granularidade.Mes, filtro.Granularidade);
        Assert.Equal(
            [
                new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1),
            ],
            filtro.Buckets());
    }
}
