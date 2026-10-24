import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { Tenant } from './tenants.api';
import { TenantFormPage } from './tenant-form.page';

const CNPJ_VALIDO = '11222333000181';

const lojaA: Tenant = {
  idPublico: 'id-loja-a',
  nome: 'Loja A',
  cnpj: CNPJ_VALIDO,
  ativo: true,
  criadoEm: '2026-10-01T10:00:00Z',
  alteradoEm: null,
  versao: 'AAAAAAAAB9E=',
};

describe('TenantFormPage', () => {
  let fixture: ComponentFixture<TenantFormPage>;
  let http: HttpTestingController;
  let navegar: ReturnType<typeof vi.spyOn>;

  async function criar(id?: string): Promise<void> {
    TestBed.configureTestingModule({
      imports: [TenantFormPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(TenantFormPage);
    if (id) fixture.componentRef.setInput('id', id);
    await fixture.whenStable();
  }

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const tela = () => fixture.nativeElement as HTMLElement;

  function digitar(id: string, valor: string): void {
    const campo = tela().querySelector<HTMLInputElement>(`#${id}`)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  }

  async function salvar(): Promise<void> {
    tela().querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await fixture.whenStable();
  }

  async function ligarAdmin(): Promise<void> {
    tela().querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
    await fixture.whenStable();
  }

  it('CNPJ inválido bloqueia o envio e a máscara é aplicada ao digitar', async () => {
    await criar();
    digitar('tenant-nome', 'Loja Nova');
    digitar('tenant-cnpj', '11222333000199');
    await salvar();

    expect(tela().querySelector<HTMLInputElement>('#tenant-cnpj')!.value).toBe('11.222.333/0001-99');
    expect(tela().querySelector('#tenant-cnpj-erro')?.textContent).toContain('CNPJ inválido.');
    http.expectNone('/api/admin/tenants');
  });

  it('criação sem Admin inicial envia nome e CNPJ só com dígitos e volta à lista', async () => {
    await criar();
    digitar('tenant-nome', '  Loja Nova  ');
    digitar('tenant-cnpj', '11.222.333/0001-81');
    await salvar();

    const req = http.expectOne('/api/admin/tenants');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ nome: 'Loja Nova', cnpj: CNPJ_VALIDO });
    req.flush({ ...lojaA, nome: 'Loja Nova' }, { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    expect(navegar).toHaveBeenCalledWith('/admin/tenants');
  });

  it('criação com Admin inicial envia adminInicial com nome, e-mail e senha', async () => {
    await criar();
    digitar('tenant-nome', 'Loja Nova');
    digitar('tenant-cnpj', CNPJ_VALIDO);
    await ligarAdmin();
    digitar('admin-nome', 'Ana Admin');
    digitar('admin-email', 'ana@loja.com');
    digitar('admin-senha', 'SenhaInicial1');
    await salvar();

    const req = http.expectOne('/api/admin/tenants');
    expect(req.request.body).toEqual({
      nome: 'Loja Nova',
      cnpj: CNPJ_VALIDO,
      adminInicial: { nome: 'Ana Admin', email: 'ana@loja.com', senhaInicial: 'SenhaInicial1' },
    });
    req.flush(lojaA, { status: 201, statusText: 'Created' });
    await fixture.whenStable();
    expect(navegar).toHaveBeenCalledWith('/admin/tenants');
  });

  it('CNPJ_DUPLICADO e EMAIL_EM_USO aparecem no campo certo', async () => {
    await criar();
    digitar('tenant-nome', 'Loja Nova');
    digitar('tenant-cnpj', CNPJ_VALIDO);
    await ligarAdmin();
    digitar('admin-nome', 'Ana Admin');
    digitar('admin-email', 'ana@loja.com');
    digitar('admin-senha', 'SenhaInicial1');

    await salvar();
    responderProblema(http.expectOne('/api/admin/tenants'), 409, 'CNPJ_DUPLICADO');
    await fixture.whenStable();
    expect(tela().querySelector('#tenant-cnpj-erro')?.textContent).toContain('CNPJ já cadastrado');
    expect(tela().querySelector('#admin-email-erro')).toBeNull();

    await salvar();
    responderProblema(http.expectOne('/api/admin/tenants'), 409, 'EMAIL_EM_USO');
    await fixture.whenStable();
    expect(tela().querySelector('#admin-email-erro')?.textContent).toContain('E-mail já está em uso');
    expect(tela().querySelector('#tenant-cnpj-erro')).toBeNull();
    expect(navegar).not.toHaveBeenCalled();
  });

  it('edição envia a versão lida e CONFLITO_VERSAO mostra o aviso de recarregar', async () => {
    await criar('id-loja-a');
    http.expectOne('/api/admin/tenants/id-loja-a').flush(lojaA);
    await fixture.whenStable();
    expect(tela().querySelector<HTMLInputElement>('#tenant-cnpj')!.value).toBe('11.222.333/0001-81');
    expect(tela().querySelector('input[type=checkbox]')).toBeNull();

    digitar('tenant-nome', 'Loja A Renomeada');
    await salvar();
    const req = http.expectOne('/api/admin/tenants/id-loja-a');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ nome: 'Loja A Renomeada', cnpj: CNPJ_VALIDO, versao: 'AAAAAAAAB9E=' });
    responderProblema(req, 409, 'CONFLITO_VERSAO');
    await fixture.whenStable();

    const aviso = tela().querySelector('[role=alert]')!;
    expect(aviso.textContent).toContain('Registro alterado por outra pessoa. Recarregue.');
    aviso.querySelector('button')!.click();
    http.expectOne('/api/admin/tenants/id-loja-a').flush({ ...lojaA, versao: 'AAAAAAAAB+A=' });
    await fixture.whenStable();
    expect(tela().querySelector('[role=alert]')).toBeNull();
  });
});
