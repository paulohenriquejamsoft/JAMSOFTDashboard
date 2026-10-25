import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideFormatacaoPtBr } from '../../core/formatacao/formatacao';
import { RankingsResposta } from './painel.service';
import { RankingsComponent } from './rankings.component';

const DADOS: RankingsResposta = {
  totalFaturamento: 3000,
  vendedores: [
    { nome: 'Ana', faturamento: 1234.56, vendas: 1200, participacao: 41.15 },
    { nome: 'Não informado', faturamento: 765.44, vendas: 3, participacao: 25.51 },
  ],
  clientes: [{ nome: 'Cliente X', faturamento: 3000, vendas: 10, participacao: 100 }],
  empresas: [{ nome: 'Matriz', faturamento: 3000, vendas: 10, participacao: 100 }],
};

describe('RankingsComponent', () => {
  let fixture: ComponentFixture<RankingsComponent>;

  async function criar(dados: RankingsResposta = DADOS, mostrarEmpresas = true): Promise<void> {
    TestBed.configureTestingModule({ imports: [RankingsComponent], providers: [provideFormatacaoPtBr()] });
    fixture = TestBed.createComponent(RankingsComponent);
    fixture.componentRef.setInput('dados', dados);
    fixture.componentRef.setInput('mostrarEmpresas', mostrarEmpresas);
    await fixture.whenStable();
  }

  const raiz = () => fixture.nativeElement as HTMLElement;
  const ranking = (id: string) => raiz().querySelector<HTMLElement>(`[data-ranking=${id}]`);
  const texto = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim().replaceAll(String.fromCharCode(0xa0), ' ');
  const linhas = (id: string) =>
    [...ranking(id)!.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map(texto));

  it('cada linha mostra nome, faturamento em BRL, vendas e participação formatados', async () => {
    await criar();

    expect(linhas('vendedores')).toEqual([
      ['Ana', 'R$ 1.234,56', '1.200', '41,15%'],
      ['Não informado', 'R$ 765,44', '3', '25,51%'],
    ]);
    expect(linhas('clientes')).toEqual([['Cliente X', 'R$ 3.000,00', '10', '100,00%']]);
  });

  it('barra inline tem largura igual à participação', async () => {
    await criar();

    const larguras = [...ranking('vendedores')!.querySelectorAll<HTMLElement>('.barra')].map((b) => b.style.width);
    expect(larguras).toEqual(['41.15%', '25.51%']);
  });

  it('tenant com 1 empresa não mostra a tabela de Empresas', async () => {
    await criar(DADOS, false);

    expect(ranking('empresas')).toBeNull();
    expect([...raiz().querySelectorAll('caption')].map(texto)).toEqual(['Vendedores', 'Clientes']);
  });

  it('lista vazia mostra o estado vazio', async () => {
    await criar({ ...DADOS, clientes: [] });

    expect(ranking('clientes')!.querySelectorAll('tbody tr').length).toBe(0);
    expect(texto(ranking('clientes')!.querySelector('.vazio')!)).toBe('Nenhuma venda no período.');
    expect(ranking('vendedores')!.querySelector('.vazio')).toBeNull();
  });

  it('cada tabela tem <caption> e cabeçalhos de coluna', async () => {
    await criar();

    expect([...raiz().querySelectorAll('caption')].map(texto)).toEqual(['Vendedores', 'Clientes', 'Empresas']);
    for (const id of ['vendedores', 'clientes', 'empresas']) {
      const cabecalhos = [...ranking(id)!.querySelectorAll('thead th')];
      expect(cabecalhos.map(texto), id).toEqual(['Nome', 'Faturamento', 'Vendas', 'Participação']);
      expect(cabecalhos.every((th) => th.getAttribute('scope') === 'col'), id).toBe(true);
    }
  });
});
