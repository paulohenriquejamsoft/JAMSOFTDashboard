import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { isSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AbstractControl } from '@angular/forms';
import { Router, provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { Paginado } from '../../shared/paginado';
import { Tenant } from '../tenants/tenants.api';
import { Coletor } from './coletores.api';
import { ColetorFormPage } from './coletor-form.page';

const SEGREDO = 'segredo-super-secreto-0123456789abcdef';

function tenant(idPublico: string, nome: string): Tenant {
  return { idPublico, nome, cnpj: '11222333000181', ativo: true, criadoEm: '2026-10-01T10:00:00Z', alteradoEm: null, versao: 'AA==' };
}

const ATIVOS = [tenant('t-a', 'Loja A'), tenant('t-b', 'Loja B'), tenant('t-c', 'Loja C')];

const matriz: Coletor = {
  idPublico: 'id-matriz',
  nome: 'Servidor Matriz',
  clientId: 'client-matriz',
  ativo: true,
  tenants: [{ idPublico: 't-a', nome: 'Loja A' }],
  criadoEm: '2026-10-01T10:00:00Z',
  revogadoEm: null,
  ultimoTokenEm: null,
  versao: 'AAAAAAAAB9E=',
};

function pagina<T>(itens: T[], tamanho = 100): Paginado<T> {
  return { itens, pagina: 1, tamanho, total: itens.length };
}

describe('ColetorFormPage', () => {
  let fixture: ComponentFixture<ColetorFormPage>;
  let http: HttpTestingController;
  let navegar: ReturnType<typeof vi.spyOn>;

  async function criar(id?: string): Promise<void> {
    TestBed.configureTestingModule({
      imports: [ColetorFormPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(ColetorFormPage);
    if (id) fixture.componentRef.setInput('id', id);
    await fixture.whenStable();
    http.expectOne('/api/admin/tenants?pagina=1&situacao=Ativo&tamanho=100').flush(pagina(ATIVOS));
    await fixture.whenStable();
  }

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const tela = () => fixture.nativeElement as HTMLElement;
  const marcador = (nome: string) =>
    [...tela().querySelectorAll<HTMLLabelElement>('label.marcador')].find((l) => l.textContent?.trim() === nome)!
      .querySelector('input')!;

  function digitar(id: string, valor: string): void {
    const campo = tela().querySelector<HTMLInputElement>(`#${id}`)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  }

  async function marcar(nome: string): Promise<void> {
    marcador(nome).click();
    await fixture.whenStable();
  }

  async function salvar(): Promise<void> {
    tela().querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await fixture.whenStable();
  }

  /** Lê todos os campos do componente (signals e controles de formulário) procurando o texto. */
  function estadoContem(texto: string): boolean {
    return Object.values(fixture.componentInstance).some((valor: unknown) => {
      const lido = isSignal(valor) ? valor() : valor instanceof AbstractControl ? valor.getRawValue() : valor;
      try {
        return (JSON.stringify(lido) ?? '').includes(texto);
      } catch {
        return false;
      }
    });
  }

  async function criarColetor(): Promise<void> {
    digitar('coletor-nome', '  Servidor Matriz  ');
    await marcar('Loja A');
    await marcar('Loja C');
    await salvar();
  }

  it('criar envia nome e tenants marcados e abre o diálogo com o segredo', async () => {
    await criar();
    await criarColetor();

    const req = http.expectOne('/api/admin/coletores');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ nome: 'Servidor Matriz', tenants: ['t-a', 't-c'] });
    req.flush({ idPublico: 'id-matriz', clientId: 'client-matriz', clientSecret: SEGREDO }, { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    const dialogo = tela().querySelector('[role=dialog]')!;
    expect(dialogo.querySelector<HTMLInputElement>('#segredo-client-id')!.value).toBe('client-matriz');
    expect(dialogo.querySelector<HTMLInputElement>('#segredo-client-secret')!.value).toBe(SEGREDO);
    expect(dialogo.textContent).toContain('não será exibido novamente');
    expect(navegar).not.toHaveBeenCalled();
  });

  it('copiar grava o secret na área de transferência', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await criar();
    await criarColetor();
    http.expectOne('/api/admin/coletores').flush({ idPublico: 'id-matriz', clientId: 'client-matriz', clientSecret: SEGREDO });
    await fixture.whenStable();

    tela().querySelector<HTMLButtonElement>('button[aria-label="Copiar client secret"]')!.click();
    await fixture.whenStable();

    expect(writeText).toHaveBeenCalledWith(SEGREDO);
    expect(tela().querySelector('[role=dialog]')?.textContent).toContain('Copiado.');
  });

  it('fechar o diálogo não deixa o segredo na tela nem no estado e volta à lista', async () => {
    await criar();
    await criarColetor();
    http.expectOne('/api/admin/coletores').flush({ idPublico: 'id-matriz', clientId: 'client-matriz', clientSecret: SEGREDO });
    await fixture.whenStable();
    expect(estadoContem(SEGREDO)).toBe(true);

    [...tela().querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Fechar')!.click();
    await fixture.whenStable();

    expect(tela().querySelector('[role=dialog]')).toBeNull();
    expect(tela().innerHTML).not.toContain(SEGREDO);
    expect([...tela().querySelectorAll('input')].some((i) => i.value.includes(SEGREDO))).toBe(false);
    expect(estadoContem(SEGREDO)).toBe(false);
    expect(navegar).toHaveBeenCalledWith('/admin/coletores');
  });

  it('sem tenant marcado bloqueia o envio e mostra o erro no campo', async () => {
    await criar();
    digitar('coletor-nome', 'Servidor Matriz');
    await salvar();

    expect(tela().querySelector('#coletor-tenants-erro')?.textContent).toContain('Selecione ao menos um tenant ativo.');
    http.expectNone('/api/admin/coletores');
  });

  it('VALIDACAO da API aparece nos campos nome e tenants', async () => {
    await criar();
    await criarColetor();
    responderProblema(http.expectOne('/api/admin/coletores'), 400, 'VALIDACAO', {
      erros: { nome: ['Nome já usado.'], tenants: ['Só tenants ativos podem ser autorizados.'] },
    });
    await fixture.whenStable();

    expect(tela().querySelector('#coletor-nome-erro')?.textContent).toContain('Nome já usado.');
    expect(tela().querySelector('#coletor-tenants-erro')?.textContent).toContain('Só tenants ativos podem ser autorizados.');
    expect(tela().querySelector('[role=dialog]')).toBeNull();
  });

  it('editar envia tenants e versão lida; CONFLITO_VERSAO mostra o aviso e Recarregar relê', async () => {
    await criar('id-matriz');
    http.expectOne('/api/admin/coletores?pagina=1&tamanho=100').flush(pagina([matriz]));
    await fixture.whenStable();
    expect(tela().querySelector('h1')?.textContent).toContain('Servidor Matriz');
    expect(marcador('Loja A').checked).toBe(true);
    expect(marcador('Loja B').checked).toBe(false);

    await marcar('Loja B');
    await salvar();
    const req = http.expectOne('/api/admin/coletores/id-matriz/tenants');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ tenants: ['t-a', 't-b'], versao: 'AAAAAAAAB9E=' });
    responderProblema(req, 409, 'CONFLITO_VERSAO');
    await fixture.whenStable();

    const aviso = tela().querySelector('[role=alert]')!;
    expect(aviso.textContent).toContain('Registro alterado por outra pessoa. Recarregue.');
    expect(navegar).not.toHaveBeenCalled();

    aviso.querySelector('button')!.click();
    http
      .expectOne('/api/admin/coletores?pagina=1&tamanho=100')
      .flush(pagina([{ ...matriz, tenants: [{ idPublico: 't-c', nome: 'Loja C' }], versao: 'AAAAAAAAB+A=' }]));
    await fixture.whenStable();
    expect(tela().querySelector('[role=alert]')).toBeNull();
    expect(marcador('Loja A').checked).toBe(false);
    expect(marcador('Loja C').checked).toBe(true);

    await salvar();
    const segunda = http.expectOne('/api/admin/coletores/id-matriz/tenants');
    expect(segunda.request.body).toEqual({ tenants: ['t-c'], versao: 'AAAAAAAAB+A=' });
    segunda.flush({ ...matriz, tenants: [{ idPublico: 't-c', nome: 'Loja C' }] });
    await fixture.whenStable();
    expect(navegar).toHaveBeenCalledWith('/admin/coletores');
  });
});
