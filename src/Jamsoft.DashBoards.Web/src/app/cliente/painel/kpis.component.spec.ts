import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideFormatacaoPtBr } from '../../core/formatacao/formatacao';
import { KpisComponent } from './kpis.component';
import { Indicador, KpisResposta } from './painel.service';

type Kpis = KpisResposta['kpis'];
const ind = (atual: number, variacao: number | null = null): Indicador => ({ atual, anterior: 0, variacao });

const KPIS: Kpis = {
  faturamento: ind(1234.56, 12.3),
  vendas: ind(1500, -5),
  ticketMedio: ind(82.3, null),
  itensPorVenda: ind(3.456, 0),
  percentualCanceladas: ind(4.5, 2),
};

describe('KpisComponent', () => {
  let fixture: ComponentFixture<KpisComponent>;

  async function criar(kpis: Kpis = KPIS): Promise<void> {
    TestBed.configureTestingModule({ imports: [KpisComponent], providers: [provideFormatacaoPtBr()] });
    fixture = TestBed.createComponent(KpisComponent);
    fixture.componentRef.setInput('kpis', kpis);
    await fixture.whenStable();
  }

  const kpi = (id: keyof Kpis) => (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-kpi=${id}]`)!;
  const texto = (el: Element | null) => el!.textContent!.replace(/\s+/g, ' ').trim().replaceAll(String.fromCharCode(0xa0), ' ');

  it('formata faturamento e ticket em BRL, vendas inteiro, itens com 2 casas e % canceladas com "%"', async () => {
    await criar();

    expect(texto(kpi('faturamento').querySelector('.valor'))).toBe('R$ 1.234,56');
    expect(texto(kpi('ticketMedio').querySelector('.valor'))).toBe('R$ 82,30');
    expect(texto(kpi('vendas').querySelector('.valor'))).toBe('1.500');
    expect(texto(kpi('itensPorVenda').querySelector('.valor'))).toBe('3,46');
    expect(texto(kpi('percentualCanceladas').querySelector('.valor'))).toBe('4,50%');
  });

  it('variação positiva tem seta para cima, texto "+12,30%" e cor de sucesso', async () => {
    await criar();
    const variacao = kpi('faturamento').querySelector('.variacao')!;

    expect(texto(variacao.querySelector('.seta'))).toBe('▲');
    expect(variacao.querySelector('.seta')!.getAttribute('aria-hidden')).toBe('true');
    expect(texto(variacao.querySelector('.texto'))).toBe('+12,30%');
    expect(variacao.classList).toContain('sucesso');
    expect(texto(variacao.querySelector('.visualmente-oculto'))).toBe('de alta em relação ao período anterior');
  });

  it('variação positiva usa o verde de texto do painel, com contraste ≥ 4,5:1 sobre o branco do bloco (UI-06)', async () => {
    await criar();

    const verde = getComputedStyle(fixture.nativeElement as HTMLElement).getPropertyValue('--kpi-sucesso-texto').trim();
    const variacao = kpi('faturamento').querySelector('.variacao')!;

    expect(verde).toBe('#2e7d32');
    expect(getComputedStyle(variacao).color).toBe('var(--kpi-sucesso-texto)');
    expect(contraste(verde, '#ffffff')).toBeGreaterThanOrEqual(4.5);
  });

  it('variação negativa tem seta para baixo, texto "-5,00%" e cor de perigo', async () => {
    await criar();
    const variacao = kpi('vendas').querySelector('.variacao')!;

    expect(texto(variacao.querySelector('.seta'))).toBe('▼');
    expect(texto(variacao.querySelector('.texto'))).toBe('-5,00%');
    expect(variacao.classList).toContain('perigo');
    expect(texto(variacao.querySelector('.visualmente-oculto'))).toBe('de queda em relação ao período anterior');
  });

  it('variação nula mostra "—" sem seta nem cor', async () => {
    await criar();
    const variacao = kpi('ticketMedio').querySelector('.variacao')!;

    expect(texto(variacao.querySelector('.texto'))).toBe('—');
    expect(variacao.querySelector('.seta')).toBeNull();
    expect(variacao.classList).not.toContain('sucesso');
    expect(variacao.classList).not.toContain('perigo');
  });

  it('cada bloco tem rótulo acessível', async () => {
    await criar();
    const raiz = fixture.nativeElement as HTMLElement;

    const rotulos = [...raiz.querySelectorAll('[role=group]')].map(
      (grupo) => texto(raiz.querySelector(`#${grupo.getAttribute('aria-labelledby')}`)),
    );
    expect(rotulos).toEqual(['Faturamento', 'Vendas', 'Ticket médio', 'Itens por venda', 'Vendas canceladas']);
    const descricao = kpi('faturamento').getAttribute('aria-describedby')!;
    expect(texto(raiz.querySelector(`#${descricao}`))).toContain('+12,30% de alta em relação ao período anterior');
  });
});

/** Razão de contraste WCAG 2.x entre duas cores `#rrggbb`. */
function contraste(a: string, b: string): number {
  const luminancia = (cor: string) => {
    const [r, g, bl] = [1, 3, 5].map((i) => {
      const s = parseInt(cor.slice(i, i + 2), 16) / 255;
      return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [claro, escuro] = [luminancia(a), luminancia(b)].sort((x, y) => y - x);
  return (claro + 0.05) / (escuro + 0.05);
}
