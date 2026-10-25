import { Provider } from '@angular/core';
import { provideEchartsCore } from 'ngx-echarts';
import { LineChart, PieChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';

// Só os gráficos e componentes usados no painel (AD-017). Este arquivo é importado apenas pelos componentes de
// gráfico, que vivem na rota lazy do painel; assim o ECharts nunca entra no chunk inicial.
echarts.use([LineChart, PieChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer]);

/** Provider do `ngx-echarts` com o núcleo modular do ECharts. Declarado no próprio componente de gráfico. */
export function provideGraficos(): Provider {
  return provideEchartsCore({ echarts });
}
