import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { RELOGIO } from '../../shared/layout/status-dados.component';
import { Empresa } from '../empresas/empresas.api';
import { FiltroPainelComponent } from './filtro-painel.component';
import { FiltroPainel, Vendedor } from './painel.service';

const EMPRESA_A = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const EMPRESA_B = '9a7b1c2d-0000-4000-8000-000000000002';
const empresa = (idPublico: string, nome: string): Empresa => ({ idPublico, codEmpresa: 1, nome, nomeManual: false });

/** Registra o que o filtro emite. */
@Component({
  imports: [FiltroPainelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-filtro-painel (filtrar)="emitidos.push($event)" (empresasCarregadas)="quantidades.push($event)" />`,
})
class HostComponent {
  readonly emitidos: FiltroPainel[] = [];
  readonly quantidades: number[] = [];
}

describe('FiltroPainelComponent', () => {
  let harness: RouterTestingHarness;
  let host: HostComponent;
  let http: HttpTestingController;

  /** Abre `/painel` com o relógio em `agora` e responde `/api/empresas` e `/api/painel/vendedores`. */
  async function abrir(
    url: string,
    agora: string,
    empresas: Empresa[] = [empresa(EMPRESA_A, 'Matriz')],
    vendedores: Vendedor[] = [],
  ) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'painel', component: HostComponent }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: RELOGIO, useValue: () => Date.parse(agora) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    host = await harness.navigateByUrl(url, HostComponent);
    http.expectOne('/api/empresas').flush(empresas);
    http.expectOne('/api/painel/vendedores').flush(vendedores);
    await harness.fixture.whenStable();
  }

  afterEach(() => http.verify());

  const tela = () => harness.routeNativeElement!;
  const botao = (texto: string) =>
    [...tela().querySelectorAll('button')].find((b) => b.textContent?.trim() === texto)!;
  const campo = (id: string) => tela().querySelector<HTMLInputElement>(`#${id}`)!;
  const ultimo = () => host.emitidos.at(-1);
  const url = () => TestBed.inject(Router).url;

  async function mudarData(id: string, valor: string): Promise<void> {
    campo(id).value = valor;
    campo(id).dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
  }

  it('cada atalho gera as datas esperadas pelo relógio de São Paulo', async () => {
    // 24/10/2026 12:00 em São Paulo.
    await abrir('/painel', '2026-10-24T15:00:00Z');

    const casos: [string, FiltroPainel][] = [
      ['Últimos 7 dias', { inicio: '2026-10-18', fim: '2026-10-24', empresas: [], tipos: [], vendedores: [] }],
      ['Últimos 30 dias', { inicio: '2026-09-25', fim: '2026-10-24', empresas: [], tipos: [], vendedores: [] }],
      ['Mês anterior', { inicio: '2026-09-01', fim: '2026-09-30', empresas: [], tipos: [], vendedores: [] }],
      ['Este mês', { inicio: '2026-10-01', fim: '2026-10-24', empresas: [], tipos: [], vendedores: [] }],
    ];
    for (const [atalho, esperado] of casos) {
      botao(atalho).click();
      await harness.fixture.whenStable();
      expect(ultimo(), atalho).toEqual(esperado);
      expect(campo('filtro-inicio').value, atalho).toBe(esperado.inicio);
      expect(campo('filtro-fim').value, atalho).toBe(esperado.fim);
    }
  });

  it('sem parâmetros abre no mês corrente de São Paulo, não no de UTC', async () => {
    // 01/10/2026 02:00 UTC = 30/09/2026 23:00 em São Paulo.
    await abrir('/painel', '2026-10-01T02:00:00Z');

    expect(host.emitidos).toEqual([{ inicio: '2026-09-01', fim: '2026-09-30', empresas: [], tipos: [], vendedores: [] }]);
  });

  it('mudar o período emite o novo filtro e grava na URL', async () => {
    await abrir('/painel', '2026-10-24T15:00:00Z');

    await mudarData('filtro-inicio', '2026-10-10');

    expect(ultimo()).toEqual({ inicio: '2026-10-10', fim: '2026-10-24', empresas: [], tipos: [], vendedores: [] });
    expect(url()).toBe('/painel?inicio=2026-10-10&fim=2026-10-24');
  });

  it('abrir com ?inicio&fim&empresas restaura o filtro', async () => {
    await abrir(
      `/painel?inicio=2026-08-01&fim=2026-08-31&empresas=${EMPRESA_A}&empresas=${EMPRESA_B}`,
      '2026-10-24T15:00:00Z',
      [empresa(EMPRESA_A, 'Matriz'), empresa(EMPRESA_B, 'Filial')],
    );

    expect(host.emitidos).toEqual([{ inicio: '2026-08-01', fim: '2026-08-31', empresas: [EMPRESA_A, EMPRESA_B], tipos: [], vendedores: [] }]);
    expect(campo('filtro-inicio').value).toBe('2026-08-01');
    expect(campo('filtro-fim').value).toBe('2026-08-31');
    const marcadas = [...tela().querySelectorAll<HTMLInputElement>('.empresas input[type=checkbox]')].map((c) => c.checked);
    expect(marcadas).toEqual([true, true]);
  });

  it('fim antes do início mostra a mensagem e não emite', async () => {
    await abrir('/painel', '2026-10-24T15:00:00Z');
    const antes = host.emitidos.length;

    await mudarData('filtro-fim', '2026-09-30');

    expect(tela().querySelector('[role=alert]')?.textContent?.trim()).toBe('A data final não pode ser anterior à inicial.');
    expect(host.emitidos.length).toBe(antes);
    expect(url()).toBe('/painel');
  });

  it('período de 25 meses mostra a mensagem e não emite; exatamente 24 meses é aceito', async () => {
    await abrir('/painel', '2026-10-24T15:00:00Z');
    await mudarData('filtro-inicio', '2024-10-01');
    const antes = host.emitidos.length;

    await mudarData('filtro-fim', '2026-10-01');

    expect(tela().querySelector('[role=alert]')?.textContent?.trim()).toBe('O período não pode passar de 24 meses.');
    expect(host.emitidos.length).toBe(antes);

    await mudarData('filtro-fim', '2026-09-30');
    expect(tela().querySelector('[role=alert]')).toBeNull();
    expect(ultimo()).toEqual({ inicio: '2024-10-01', fim: '2026-09-30', empresas: [], tipos: [], vendedores: [] });
  });

  it('informa quantas empresas o tenant tem', async () => {
    await abrir('/painel', '2026-10-24T15:00:00Z', [empresa(EMPRESA_A, 'Matriz'), empresa(EMPRESA_B, 'Filial')]);

    expect(host.quantidades).toEqual([2]);
  });

  it('seletor de empresas fica oculto com 1 empresa e, com 2, marcar uma restringe o filtro e a URL', async () => {
    await abrir('/painel', '2026-10-24T15:00:00Z');
    expect(tela().querySelector('.empresas')).toBeNull();

    TestBed.resetTestingModule();
    await abrir('/painel', '2026-10-24T15:00:00Z', [empresa(EMPRESA_A, 'Matriz'), empresa(EMPRESA_B, 'Filial')]);
    const caixas = [...tela().querySelectorAll<HTMLInputElement>('.empresas input[type=checkbox]')];
    expect(caixas.map((c) => c.parentElement!.textContent!.trim())).toEqual(['Matriz', 'Filial']);

    caixas[1].click();
    await harness.fixture.whenStable();

    expect(ultimo()).toEqual({ inicio: '2026-10-01', fim: '2026-10-24', empresas: [EMPRESA_B], tipos: [], vendedores: [] });
    expect(url()).toBe(`/painel?inicio=2026-10-01&fim=2026-10-24&empresas=${EMPRESA_B}`);
  });

  it('marcar tipos de documento restringe o filtro e a URL, na ordem do domínio (DASH-10)', async () => {
    await abrir('/painel', '2026-10-24T15:00:00Z');
    const caixas = [...tela().querySelectorAll<HTMLInputElement>('fieldset.tipos input[type=checkbox]')];
    expect(caixas.map((c) => c.parentElement!.textContent!.trim())).toEqual(['NF-e', 'NFC-e', 'Pedido']);

    caixas[2].click();
    caixas[0].click();
    await harness.fixture.whenStable();

    expect(ultimo()).toEqual({ inicio: '2026-10-01', fim: '2026-10-24', empresas: [], tipos: ['NF-e', 'Pedido'], vendedores: [] });
    expect(url()).toBe('/painel?inicio=2026-10-01&fim=2026-10-24&tipos=NF-e&tipos=Pedido');
  });

  it('seletor de vendedor fica oculto sem vendedores; escolher um restringe o filtro e a URL (DASH-10)', async () => {
    await abrir('/painel', '2026-10-24T15:00:00Z');
    expect(tela().querySelector('#filtro-vendedor')).toBeNull();

    TestBed.resetTestingModule();
    await abrir('/painel', '2026-10-24T15:00:00Z', undefined, [{ codigo: 1, nome: 'Ana' }, { codigo: 2, nome: 'Bruno' }]);
    const seletor = tela().querySelector<HTMLSelectElement>('#filtro-vendedor')!;
    expect([...seletor.options].map((o) => o.textContent!.trim())).toEqual(['Todos', 'Ana', 'Bruno']);

    seletor.value = '2';
    seletor.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();

    expect(ultimo()).toEqual({ inicio: '2026-10-01', fim: '2026-10-24', empresas: [], tipos: [], vendedores: [2] });
    expect(url()).toBe('/painel?inicio=2026-10-01&fim=2026-10-24&vendedor=2');

    seletor.value = '';
    seletor.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    expect(ultimo()?.vendedores).toEqual([]);
  });

  it('abrir com ?tipos&vendedor restaura o filtro e ignora tipo desconhecido', async () => {
    await abrir('/painel?inicio=2026-08-01&fim=2026-08-31&tipos=Pedido&tipos=CT-e&vendedor=1', '2026-10-24T15:00:00Z', undefined, [
      { codigo: 1, nome: 'Ana' },
    ]);

    expect(host.emitidos).toEqual([
      { inicio: '2026-08-01', fim: '2026-08-31', empresas: [], tipos: ['Pedido'], vendedores: [1] },
    ]);
    const marcadas = [...tela().querySelectorAll<HTMLInputElement>('fieldset.tipos input[type=checkbox]')].map((c) => c.checked);
    expect(marcadas).toEqual([false, false, true]);
    expect(tela().querySelector<HTMLSelectElement>('#filtro-vendedor')!.value).toBe('1');
  });
});
