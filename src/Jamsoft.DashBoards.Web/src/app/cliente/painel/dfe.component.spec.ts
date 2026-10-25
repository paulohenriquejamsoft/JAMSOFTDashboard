import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EchartsFalso, trocarEcharts } from '../../../testes/echarts-falso';
import { provideFormatacaoPtBr } from '../../core/formatacao/formatacao';
import { DfeComponent } from './dfe.component';
import { DfeResposta } from './painel.service';

interface Opcoes {
  series: { type: string; data: { name: string; value: number }[] }[];
}

const DADOS: DfeResposta = {
  tipos: [
    { tipo: 'NF-e', faturamento: 1234.56, vendas: 3, participacao: 30.25 },
    { tipo: 'NFC-e', faturamento: 2846.7, vendas: 7, participacao: 69.75 },
  ],
};

describe('DfeComponent', () => {
  let fixture: ComponentFixture<DfeComponent>;

  async function criar(dados: DfeResposta): Promise<Opcoes> {
    TestBed.configureTestingModule({ imports: [DfeComponent], providers: [provideFormatacaoPtBr()] });
    trocarEcharts(DfeComponent);
    fixture = TestBed.createComponent(DfeComponent);
    fixture.componentRef.setInput('dados', dados);
    await fixture.whenStable();
    const grafico = fixture.debugElement.query((el) => el.injector.get(EchartsFalso, null) !== null);
    return grafico.injector.get(EchartsFalso).options() as unknown as Opcoes;
  }

  const texto = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim().replaceAll(String.fromCharCode(0xa0), ' ');
  const legenda = (tipo: string) =>
    (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(`li[data-tipo="${tipo}"]`)!;

  it('gera uma série de pizza com NF-e e NFC-e e os valores da resposta', async () => {
    const opcoes = await criar(DADOS);

    expect(opcoes.series).toHaveLength(1);
    expect(opcoes.series[0].type).toBe('pie');
    expect(opcoes.series[0].data.map(({ name, value }) => ({ name, value }))).toEqual([
      { name: 'NF-e', value: 1234.56 },
      { name: 'NFC-e', value: 2846.7 },
    ]);
  });

  it('legenda mostra faturamento, vendas e participação formatados', async () => {
    await criar(DADOS);

    expect(texto(legenda('NF-e'))).toBe('NF-e R$ 1.234,56 · 3 vendas · 30,25%');
    expect(texto(legenda('NFC-e'))).toBe('NFC-e R$ 2.846,70 · 7 vendas · 69,75%');
  });

  it('tipo sem vendas aparece na legenda com "R$ 0,00"', async () => {
    await criar({
      tipos: [
        { tipo: 'NF-e', faturamento: 0, vendas: 0, participacao: 0 },
        { tipo: 'NFC-e', faturamento: 500, vendas: 2, participacao: 100 },
      ],
    });

    expect(texto(legenda('NF-e'))).toBe('NF-e R$ 0,00 · 0 vendas · 0,00%');
  });
});
