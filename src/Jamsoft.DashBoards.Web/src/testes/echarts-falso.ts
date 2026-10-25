import { Directive, Type, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NgxEchartsDirective } from 'ngx-echarts';

/** No lugar do ECharts nos testes (jsdom não tem canvas nem ResizeObserver): guarda as opções do template. */
// O seletor precisa ser o mesmo do ngx-echarts para substituí-lo no template.
// eslint-disable-next-line @angular-eslint/directive-selector
@Directive({ selector: '[echarts]' })
export class EchartsFalso {
  readonly options = input<Record<string, unknown>>();
}

/** Troca o `NgxEchartsDirective` de cada componente de gráfico pelo {@link EchartsFalso}. */
export function trocarEcharts(...componentes: Type<unknown>[]): void {
  for (const componente of componentes) {
    TestBed.overrideComponent(componente, {
      remove: { imports: [NgxEchartsDirective] },
      add: { imports: [EchartsFalso] },
    });
  }
}
