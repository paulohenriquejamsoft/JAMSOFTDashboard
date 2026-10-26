import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EchartsFalso, trocarEcharts } from '../../../testes/echarts-falso';
import { provideFormatacaoPtBr } from '../../core/formatacao/formatacao';
import { EvolucaoComponent } from './evolucao.component';
import { EvolucaoResposta } from './painel.service';

interface Opcoes {
  xAxis: { data: string[] };
  yAxis: { axisLabel: { formatter: (valor: number) => string } };
  tooltip: { trigger: string; triggerOn: string; formatter: (p: { dataIndex: number }[]) => string };
  series: { name: string; type: string; data: number[]; lineStyle: { color: string; type: string } }[];
}

const DIARIO: EvolucaoResposta = {
  granularidade: 'dia',
  pontos: [
    { rotulo: '01/10', data: '2026-10-01', atual: 1500.5, anterior: 0 },
    { rotulo: '02/10', data: '2026-10-02', atual: 0, anterior: 1234.56 },
    { rotulo: '03/10', data: '2026-10-03', atual: 320, anterior: 99.9 },
  ],
};

const MENSAL: EvolucaoResposta = {
  granularidade: 'mes',
  pontos: [
    { rotulo: '11/2025', data: '2025-11-01', atual: 10, anterior: 5 },
    { rotulo: '12/2025', data: '2025-12-01', atual: 0, anterior: 0 },
    { rotulo: '01/2026', data: '2026-01-01', atual: 7, anterior: 3 },
  ],
};

describe('EvolucaoComponent', () => {
  let fixture: ComponentFixture<EvolucaoComponent>;

  async function criar(dados: EvolucaoResposta): Promise<Opcoes> {
    TestBed.configureTestingModule({ imports: [EvolucaoComponent], providers: [provideFormatacaoPtBr()] });
    trocarEcharts(EvolucaoComponent);
    document.documentElement.style.setProperty('--primary', '#f2323f');
    document.documentElement.style.setProperty('--text-muted', '#6f7482');
    fixture = TestBed.createComponent(EvolucaoComponent);
    fixture.componentRef.setInput('dados', dados);
    await fixture.whenStable();
    const grafico = fixture.debugElement.query((el) => el.injector.get(EchartsFalso, null) !== null);
    return grafico.injector.get(EchartsFalso).options() as unknown as Opcoes;
  }

  afterEach(() => {
    document.documentElement.style.removeProperty('--primary');
    document.documentElement.style.removeProperty('--text-muted');
  });

  it('gera 2 séries com os valores da resposta: atual em --primary e anterior tracejada', async () => {
    const opcoes = await criar(DIARIO);

    expect(opcoes.series.map((s) => ({ nome: s.name, tipo: s.type, dados: s.data }))).toEqual([
      { nome: 'Atual', tipo: 'line', dados: [1500.5, 0, 320] },
      { nome: 'Anterior', tipo: 'line', dados: [0, 1234.56, 99.9] },
    ]);
    expect(opcoes.series[0].lineStyle).toMatchObject({ color: '#f2323f', type: 'solid' });
    expect(opcoes.series[1].lineStyle).toMatchObject({ color: '#6f7482', type: 'dashed' });
  });

  it('eixo usa dd/MM por dia e MMM/yy por mês', async () => {
    expect((await criar(DIARIO)).xAxis.data).toEqual(['01/10', '02/10', '03/10']);

    TestBed.resetTestingModule();
    expect((await criar(MENSAL)).xAxis.data).toEqual(['nov/25', 'dez/25', 'jan/26']);
  });

  it('tooltip por eixo devolve a data, "Atual R$ …" e "Anterior R$ …"', async () => {
    const opcoes = await criar(DIARIO);

    expect(opcoes.tooltip.trigger).toBe('axis');
    const texto = opcoes.tooltip.formatter([{ dataIndex: 1 }]).replaceAll(String.fromCharCode(0xa0), ' ');
    expect(texto).toBe('02/10/2026<br/>Atual R$ 0,00<br/>Anterior R$ 1.234,56');
  });

  it('eixo de valores usa o separador de milhar pt-BR (2.500)', async () => {
    const { formatter } = (await criar(DIARIO)).yAxis.axisLabel;

    expect([0, 2500, 1234567, 1500.5].map(formatter)).toEqual(['0', '2.500', '1.234.567', '1.500,5']);
  });

  it('tooltip abre com cursor e com toque', async () => {
    const opcoes = await criar(DIARIO);

    expect(opcoes.tooltip.triggerOn.split('|').sort()).toEqual(['click', 'mousemove']);
  });
});
