import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { Paginado } from '../../shared/paginado';
import { Tenant } from './tenants.api';
import { TenantsListaPage } from './tenants-lista.page';

function tenant(nome: string, ativo = true): Tenant {
  return {
    idPublico: `id-${nome.toLowerCase().replace(/\s/g, '-')}`,
    nome,
    cnpj: '11222333000181',
    ativo,
    criadoEm: '2026-10-01T10:00:00Z',
    alteradoEm: null,
    versao: 'AAAAAAAAB9E=',
  };
}

function pagina(itens: Tenant[], numero = 1, total = itens.length): Paginado<Tenant> {
  return { itens, pagina: numero, tamanho: 20, total };
}

describe('TenantsListaPage', () => {
  let fixture: ComponentFixture<TenantsListaPage>;
  let http: HttpTestingController;

  async function criar(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [TenantsListaPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TenantsListaPage);
    if (vi.isFakeTimers()) {
      await vi.advanceTimersByTimeAsync(0);
      fixture.detectChanges();
    } else {
      await fixture.whenStable();
    }
  }

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  const tela = () => fixture.nativeElement as HTMLElement;
  /** Células de cada linha, separadas por espaço. */
  const linhas = () =>
    [...tela().querySelectorAll('tbody tr')].map((tr) =>
      [...tr.querySelectorAll('td')].map((td) => td.textContent!.replace(/\s+/g, ' ').trim()).join(' '),
    );
  const botao = (texto: string) =>
    [...tela().querySelectorAll<HTMLElement>('button, a')].find((b) => b.textContent?.trim() === texto);
  const consulta = (params: string) => (r: { urlWithParams: string }) => r.urlWithParams === `/api/admin/tenants?${params}`;

  async function responder(req: TestRequest, corpo: Paginado<Tenant>): Promise<void> {
    req.flush(corpo);
    await fixture.whenStable();
  }

  it('busca espera o debounce e consulta uma vez com o último texto', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
    await criar();
    http.expectOne(consulta('pagina=1')).flush(pagina([tenant('Loja A')]));

    const campo = tela().querySelector<HTMLInputElement>('#tenants-busca')!;
    for (const texto of ['Lo', 'Loj', 'Loja B']) {
      campo.value = texto;
      campo.dispatchEvent(new Event('input'));
      await vi.advanceTimersByTimeAsync(100);
    }
    http.expectNone(() => true);

    await vi.advanceTimersByTimeAsync(300);
    http.expectOne(consulta('pagina=1&busca=Loja%20B')).flush(pagina([tenant('Loja B')]));
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(linhas()).toEqual(['Loja B 11.222.333/0001-81 Ativo Editar Inativar Excluir']);
  });

  it('filtro de situação consulta com situacao e volta à página 1', async () => {
    await criar();
    await responder(http.expectOne(consulta('pagina=1')), pagina([tenant('Loja A')]));

    const select = tela().querySelector<HTMLSelectElement>('#tenants-situacao')!;
    select.value = 'Inativo';
    select.dispatchEvent(new Event('change'));
    await responder(http.expectOne(consulta('pagina=1&situacao=Inativo')), pagina([tenant('Loja Parada', false)]));

    expect(linhas()).toEqual(['Loja Parada 11.222.333/0001-81 Inativo Editar Reativar Excluir']);
  });

  it('paginação pede a página escolhida', async () => {
    await criar();
    await responder(http.expectOne(consulta('pagina=1')), pagina([tenant('Loja A')], 1, 45));

    tela().querySelector<HTMLButtonElement>('button[aria-label="Página 3"]')!.click();
    await responder(http.expectOne(consulta('pagina=3')), pagina([tenant('Loja Z')], 3, 45));

    expect(linhas()).toEqual(['Loja Z 11.222.333/0001-81 Ativo Editar Inativar Excluir']);
    expect(tela().querySelector('button[aria-current=page]')?.textContent?.trim()).toBe('3');
    expect(tela().querySelector<HTMLButtonElement>('button[aria-label="Próxima página"]')!.disabled).toBe(true);
  });

  it('lista vazia mostra o estado vazio com a ação Cadastrar tenant', async () => {
    await criar();
    await responder(http.expectOne(consulta('pagina=1')), pagina([], 1, 0));

    expect(tela().textContent).toContain('Nenhum tenant encontrado.');
    expect(botao('Cadastrar tenant')?.getAttribute('href')).toBe('/admin/tenants/novo');
    expect(tela().querySelector('table')).toBeNull();
  });

  it('erro mostra a mensagem e Tentar novamente refaz a consulta', async () => {
    await criar();
    responderProblema(http.expectOne(consulta('pagina=1')), 500, 'ERRO_INTERNO', { traceId: 'abc123' });
    await fixture.whenStable();

    expect(tela().querySelector('[role=alert]')?.textContent).toContain('Erro inesperado (código abc123)');

    botao('Tentar novamente')!.click();
    await responder(http.expectOne(consulta('pagina=1')), pagina([tenant('Loja A')]));
    expect(linhas()).toEqual(['Loja A 11.222.333/0001-81 Ativo Editar Inativar Excluir']);
  });

  it('inativar pede confirmação, chama a API e atualiza a linha', async () => {
    await criar();
    await responder(http.expectOne(consulta('pagina=1')), pagina([tenant('Loja A'), tenant('Loja B')]));

    botao('Inativar')!.click();
    await fixture.whenStable();
    const dialogo = tela().querySelector('[role=alertdialog]')!;
    expect(dialogo.textContent).toContain('Inativar tenant');
    expect(dialogo.textContent).toContain('Os usuários de Loja A não conseguirão entrar');
    http.expectNone('/api/admin/tenants/id-loja-a/inativar');

    [...dialogo.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Inativar')!.click();
    const req = http.expectOne('/api/admin/tenants/id-loja-a/inativar');
    expect(req.request.method).toBe('POST');
    req.flush(tenant('Loja A', false));
    await fixture.whenStable();

    expect(tela().querySelector('[role=alertdialog]')).toBeNull();
    expect(linhas()).toEqual([
      'Loja A 11.222.333/0001-81 Inativo Editar Reativar Excluir',
      'Loja B 11.222.333/0001-81 Ativo Editar Inativar Excluir',
    ]);
  });

  it('reativar tenant inativo chama /reativar e atualiza a linha', async () => {
    await criar();
    await responder(http.expectOne(consulta('pagina=1')), pagina([tenant('Loja A', false)]));

    botao('Reativar')!.click();
    await fixture.whenStable();
    const dialogo = tela().querySelector('[role=alertdialog]')!;
    [...dialogo.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Reativar')!.click();
    http.expectOne('/api/admin/tenants/id-loja-a/reativar').flush(tenant('Loja A', true));
    await fixture.whenStable();

    expect(linhas()).toEqual(['Loja A 11.222.333/0001-81 Ativo Editar Inativar Excluir']);
  });
});
