import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { EChartsCoreOption } from 'echarts/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import { provideGraficos } from './graficos/echarts';
import { estiloTexto, lerTemaGraficos } from './graficos/tema-graficos';
import { DfeResposta } from './painel.service';

/**
 * Rosca por tipo de documento — NF-e, NFC-e e Pedido (DFE-01) — com legenda em HTML: faturamento, nº de vendas e participação de cada tipo. A
 * legenda traz o nome e os números, então a identidade nunca depende só da cor.
 */
@Component({
  selector: 'app-dfe',
  imports: [CurrencyPipe, DecimalPipe, NgxEchartsDirective],
  providers: [provideGraficos()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
      padding: var(--espaco-2) var(--espaco-4) var(--espaco-4);
    }
    .grafico {
      width: 100%;
      height: 220px;
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--espaco-2);
    }
    li {
      display: grid;
      grid-template-columns: 12px minmax(0, 1fr) auto;
      align-items: center;
      gap: var(--espaco-2);
      font-size: 13px;
    }
    .cor {
      width: 12px;
      height: 12px;
      border-radius: 3px;
    }
    .tipo {
      font-weight: 600;
      color: var(--text-primary);
    }
    .numeros {
      font-family: var(--font-mono);
      color: var(--text-secondary);
      text-align: right;
    }
  `,
  template: `
    <div echarts class="grafico" [options]="opcoes()" role="img"
      aria-label="Gráfico de rosca da divisão do faturamento entre NF-e, NFC-e e Pedido"></div>
    <ul aria-label="Legenda por tipo de documento">
      @for (item of itens(); track item.tipo) {
        <li [attr.data-tipo]="item.tipo">
          <span class="cor" aria-hidden="true" [style.background]="item.cor"></span>
          <span class="tipo">{{ item.tipo }} </span>
          <span class="numeros">
            <span class="faturamento">{{ item.faturamento | currency }}</span> ·
            <span class="vendas">{{ item.vendas | number: '1.0-0' }} {{ item.vendas === 1 ? 'venda' : 'vendas' }}</span> ·
            <span class="participacao">{{ item.participacao | number: '1.2-2' }}%</span>
          </span>
        </li>
      }
    </ul>
  `,
})
export class DfeComponent {
  readonly dados = input.required<DfeResposta>();

  /** Tipos na ordem da API, com a cor da fatia (NF-e `--primary`, NFC-e `--secundario`, Pedido `--atencao`). */
  readonly itens = computed(() => {
    const tema = lerTemaGraficos();
    const cores = [tema.primaria, tema.secundaria, tema.terciaria];
    return this.dados().tipos.map((tipo, i) => ({ ...tipo, cor: cores[i % cores.length] }));
  });

  readonly opcoes = computed<EChartsCoreOption>(() => {
    const tema = lerTemaGraficos();
    return {
      textStyle: estiloTexto(tema),
      tooltip: { trigger: 'item', triggerOn: 'mousemove|click' },
      series: [
        {
          name: 'Faturamento por tipo de documento',
          type: 'pie',
          radius: ['55%', '80%'],
          label: { show: false },
          // Espaço de 2 px entre as fatias, na cor da superfície do card.
          itemStyle: { borderColor: tema.superficie, borderWidth: 2 },
          data: this.itens().map((item) => ({ name: item.tipo, value: item.faturamento, itemStyle: { color: item.cor } })),
        },
      ],
    };
  });
}
