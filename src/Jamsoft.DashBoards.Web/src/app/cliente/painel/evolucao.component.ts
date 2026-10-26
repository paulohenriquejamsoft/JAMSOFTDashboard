import { formatCurrency, formatNumber, getCurrencySymbol } from '@angular/common';
import { ChangeDetectionStrategy, Component, DEFAULT_CURRENCY_CODE, LOCALE_ID, computed, inject, input } from '@angular/core';
import type { EChartsCoreOption } from 'echarts/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import { provideGraficos } from './graficos/echarts';
import { estiloSerieAnterior, estiloSerieAtual, estiloTexto, lerTemaGraficos } from './graficos/tema-graficos';
import { EvolucaoResposta, PontoEvolucao } from './painel.service';

const MESES = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

/** Parâmetro do formatter de tooltip com `trigger: 'axis'` (só o que é usado). */
interface ParametroTooltip {
  dataIndex: number;
}

/**
 * Faturamento por dia ou mês (ECharts, linha): período atual em `--primary` e o anterior tracejado em
 * `--text-muted`. Tooltip por eixo com a data, "Atual" e "Anterior" em BRL, por cursor ou toque (TRD-04).
 */
@Component({
  selector: 'app-evolucao',
  imports: [NgxEchartsDirective],
  providers: [provideGraficos()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
      padding: var(--espaco-2) var(--espaco-4) var(--espaco-4);
    }
    .grafico {
      width: 100%;
      height: 300px;
    }
  `,
  template: `
    <div echarts class="grafico" [options]="opcoes()" role="img"
      aria-label="Gráfico de linha do faturamento do período atual comparado ao período anterior"></div>
  `,
})
export class EvolucaoComponent {
  private readonly locale = inject(LOCALE_ID);
  private readonly moeda = inject(DEFAULT_CURRENCY_CODE);

  readonly dados = input.required<EvolucaoResposta>();

  readonly opcoes = computed<EChartsCoreOption>(() => {
    const { granularidade, pontos } = this.dados();
    const tema = lerTemaGraficos();
    const diario = granularidade === 'dia';
    return {
      textStyle: estiloTexto(tema),
      grid: { left: 8, right: 8, top: 40, bottom: 8, containLabel: true },
      legend: { top: 0, left: 0, data: ['Atual', 'Anterior'], textStyle: estiloTexto(tema) },
      tooltip: {
        trigger: 'axis',
        // Toque num ponto dispara "click" no celular; cursor dispara "mousemove".
        triggerOn: 'mousemove|click',
        formatter: (parametros: ParametroTooltip[]) => this.tooltip(pontos[parametros[0].dataIndex], diario),
      },
      xAxis: {
        type: 'category',
        boundaryGap: false,
        data: pontos.map((p) => rotuloEixo(p.data, diario)),
        axisLine: { lineStyle: { color: tema.grade } },
        axisLabel: { color: tema.textoSuave },
      },
      yAxis: {
        type: 'value',
        splitLine: { lineStyle: { color: tema.grade } },
        axisLabel: {
          color: tema.textoSuave,
          fontFamily: tema.fonteMono,
          formatter: (valor: number) => formatNumber(valor, this.locale, '1.0-2'),
        },
      },
      series: [
        { name: 'Atual', type: 'line', symbolSize: 8, data: pontos.map((p) => p.atual), ...estiloSerieAtual(tema) },
        { name: 'Anterior', type: 'line', symbolSize: 8, data: pontos.map((p) => p.anterior), ...estiloSerieAnterior(tema) },
      ],
    };
  });

  /** Data do ponto, "Atual R$ …" e "Anterior R$ …". */
  tooltip(ponto: PontoEvolucao, diario: boolean): string {
    return [rotuloTooltip(ponto.data, diario), `Atual ${this.brl(ponto.atual)}`, `Anterior ${this.brl(ponto.anterior)}`].join(
      '<br/>',
    );
  }

  private brl(valor: number): string {
    return formatCurrency(valor, this.locale, getCurrencySymbol(this.moeda, 'wide', this.locale), this.moeda);
  }
}

/** Eixo: dd/MM por dia, MMM/yy por mês (ex.: "out/26"). `data` é `yyyy-MM-dd`. */
function rotuloEixo(data: string, diario: boolean): string {
  const [ano, mes, dia] = data.split('-');
  return diario ? `${dia}/${mes}` : `${MESES[Number(mes) - 1]}/${ano.slice(2)}`;
}

/** Tooltip: dd/MM/yyyy por dia, MMM/yyyy por mês. */
function rotuloTooltip(data: string, diario: boolean): string {
  const [ano, mes, dia] = data.split('-');
  return diario ? `${dia}/${mes}/${ano}` : `${MESES[Number(mes) - 1]}/${ano}`;
}
