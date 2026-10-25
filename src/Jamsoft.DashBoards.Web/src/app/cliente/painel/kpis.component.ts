import { formatCurrency, formatNumber, getCurrencySymbol } from '@angular/common';
import { ChangeDetectionStrategy, Component, DEFAULT_CURRENCY_CODE, LOCALE_ID, computed, inject, input } from '@angular/core';
import { Indicador, KpisResposta } from './painel.service';

type Formato = 'moeda' | 'inteiro' | 'decimal' | 'percentual';

interface Bloco {
  id: string;
  rotulo: string;
  valor: string;
  variacao: Variacao;
}

interface Variacao {
  texto: string;
  /** Seta decorativa; o sentido também está no texto (sinal) e no rótulo acessível. */
  seta: '▲' | '▼' | '';
  /** Classe de cor de estado do DS; vazia quando não há variação. */
  estado: 'sucesso' | 'perigo' | '';
  descricao: string;
}

const DEFINICOES: { id: keyof KpisResposta['kpis']; rotulo: string; formato: Formato; quedaEhBoa?: boolean }[] = [
  { id: 'faturamento', rotulo: 'Faturamento', formato: 'moeda' },
  { id: 'vendas', rotulo: 'Vendas', formato: 'inteiro' },
  { id: 'ticketMedio', rotulo: 'Ticket médio', formato: 'moeda' },
  { id: 'itensPorVenda', rotulo: 'Itens por venda', formato: 'decimal' },
  // Mais cancelamento é piora: a cor inverte, a seta continua indicando o sentido do número.
  { id: 'percentualCanceladas', rotulo: 'Vendas canceladas', formato: 'percentual', quedaEhBoa: true },
];

/**
 * Os 5 KPIs do período no padrão "Relatório Painel" (DASH-02): valor formatado (BRL, inteiro, 2 casas ou %) e a
 * variação contra o período anterior (DASH-09) com seta, texto com sinal e cor de estado; "—" quando nula.
 */
@Component({
  selector: 'app-kpis',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--espaco-3);
      padding: var(--espaco-4);
    }
    @media (min-width: 1024px) {
      :host {
        grid-template-columns: repeat(5, minmax(0, 1fr));
      }
    }
    .kpi {
      display: flex;
      flex-direction: column;
      gap: var(--espaco-1);
      min-width: 0;
      padding: var(--espaco-3);
      border: 1px solid var(--borda-suave);
      border-radius: var(--raio-card);
    }
    .rotulo {
      font-size: var(--rotulo-size);
      font-weight: var(--rotulo-weight);
      letter-spacing: var(--rotulo-spacing);
      text-transform: uppercase;
      color: var(--text-muted);
    }
    .valor {
      font-family: var(--font-mono);
      font-size: 20px;
      font-weight: 700;
      color: var(--text-primary);
      overflow-wrap: anywhere;
    }
    .variacao {
      font-size: 12px;
      color: var(--text-muted);
    }
    .sucesso {
      color: var(--sucesso);
    }
    .perigo {
      color: var(--perigo);
    }
  `,
  template: `
    @for (bloco of blocos(); track bloco.id) {
      <div class="kpi" role="group" [attr.data-kpi]="bloco.id" [attr.aria-labelledby]="'kpi-' + bloco.id"
        [attr.aria-describedby]="'kpi-variacao-' + bloco.id">
        <span class="rotulo" [id]="'kpi-' + bloco.id">{{ bloco.rotulo }}</span>
        <span class="valor">{{ bloco.valor }}</span>
        <span class="variacao" [class.sucesso]="bloco.variacao.estado === 'sucesso'"
          [class.perigo]="bloco.variacao.estado === 'perigo'" [id]="'kpi-variacao-' + bloco.id">
          @if (bloco.variacao.seta) {
            <span class="seta" aria-hidden="true">{{ bloco.variacao.seta }}</span>
          }
          <span class="texto">{{ bloco.variacao.texto }}</span>
          <span class="visualmente-oculto"> {{ bloco.variacao.descricao }}</span>
        </span>
      </div>
    }
  `,
})
export class KpisComponent {
  private readonly locale = inject(LOCALE_ID);
  private readonly moeda = inject(DEFAULT_CURRENCY_CODE);

  readonly kpis = input.required<KpisResposta['kpis']>();

  readonly blocos = computed<Bloco[]>(() =>
    DEFINICOES.map(({ id, rotulo, formato, quedaEhBoa }) => {
      const indicador: Indicador = this.kpis()[id];
      return { id, rotulo, valor: this.formatar(indicador.atual, formato), variacao: this.variacao(indicador, quedaEhBoa) };
    }),
  );

  private formatar(valor: number, formato: Formato): string {
    switch (formato) {
      case 'moeda':
        return formatCurrency(valor, this.locale, getCurrencySymbol(this.moeda, 'wide', this.locale), this.moeda);
      case 'inteiro':
        return formatNumber(valor, this.locale, '1.0-0');
      case 'decimal':
        return formatNumber(valor, this.locale, '1.2-2');
      case 'percentual':
        return `${formatNumber(valor, this.locale, '1.2-2')}%`;
    }
  }

  private variacao({ variacao }: Indicador, quedaEhBoa = false): Variacao {
    if (variacao === null) {
      return { texto: '—', seta: '', estado: '', descricao: 'sem variação: período anterior sem valor' };
    }
    const numero = `${formatNumber(Math.abs(variacao), this.locale, '1.2-2')}%`;
    if (variacao === 0) return { texto: numero, seta: '', estado: '', descricao: 'igual ao período anterior' };
    const alta = variacao > 0;
    const boa = alta !== quedaEhBoa;
    return {
      texto: `${alta ? '+' : '-'}${numero}`,
      seta: alta ? '▲' : '▼',
      estado: boa ? 'sucesso' : 'perigo',
      descricao: alta ? 'de alta em relação ao período anterior' : 'de queda em relação ao período anterior',
    };
  }
}
