import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { Usuario } from './usuarios.api';
import { UsuarioFormPage } from './usuario-form.page';

const ana: Usuario = {
  idPublico: 'id-ana',
  nome: 'Ana Admin',
  email: 'ana@loja.com',
  perfil: 'Admin',
  ativo: true,
  trocarSenha: false,
  ultimoLoginEm: null,
  versao: 'AAAAAAAAB9E=',
};

describe('UsuarioFormPage', () => {
  let fixture: ComponentFixture<UsuarioFormPage>;
  let http: HttpTestingController;
  let navegar: ReturnType<typeof vi.spyOn>;

  async function criar(id?: string): Promise<void> {
    TestBed.configureTestingModule({
      imports: [UsuarioFormPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(UsuarioFormPage);
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

  function escolherPerfil(perfil: string): void {
    const select = tela().querySelector<HTMLSelectElement>('#usuario-perfil')!;
    select.value = perfil;
    select.dispatchEvent(new Event('change'));
  }

  async function salvar(): Promise<void> {
    tela().querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await fixture.whenStable();
  }

  it('só oferece os perfis Admin e Consulta', async () => {
    await criar();

    const opcoes = [...tela().querySelectorAll<HTMLOptionElement>('#usuario-perfil option')].map((o) => o.value);
    expect(opcoes).toEqual(['Admin', 'Consulta']);
  });

  it('cria com o corpo esperado; EMAIL_EM_USO aparece no campo e-mail', async () => {
    await criar();
    digitar('usuario-nome', ' Caio Consulta ');
    digitar('usuario-email', 'caio@loja.com');
    escolherPerfil('Consulta');
    digitar('usuario-senha', 'Inicial2026x');
    await salvar();

    const req = http.expectOne({ method: 'POST', url: '/api/usuarios' });
    expect(req.request.body).toEqual({
      nome: 'Caio Consulta',
      email: 'caio@loja.com',
      senhaInicial: 'Inicial2026x',
      perfil: 'Consulta',
    });
    responderProblema(req, 409, 'EMAIL_EM_USO');
    await fixture.whenStable();

    expect(tela().querySelector('#usuario-email-erro')?.textContent?.trim()).toBe('E-mail já está em uso');
    expect(tela().querySelector('#usuario-email')?.getAttribute('aria-invalid')).toBe('true');
    expect(navegar).not.toHaveBeenCalled();
  });

  it('senha inicial fora da política bloqueia o envio e mostra as regras', async () => {
    await criar();
    digitar('usuario-nome', 'Caio');
    digitar('usuario-email', 'caio@loja.com');
    digitar('usuario-senha', 'semdigitos');
    await salvar();

    expect(tela().querySelector('#usuario-senha-erro')?.textContent?.trim()).toBe(
      'A senha precisa ter ao menos um dígito.',
    );
    http.expectNone('/api/usuarios');
  });

  it('rebaixar o último Admin envia o PUT com versao e mostra a mensagem de ULTIMO_ADMIN', async () => {
    await criar('id-ana');
    http.expectOne({ method: 'GET', url: '/api/usuarios' }).flush([ana]);
    await fixture.whenStable();
    expect(tela().querySelector<HTMLInputElement>('#usuario-email')!.disabled).toBe(true);

    escolherPerfil('Consulta');
    await salvar();

    const req = http.expectOne({ method: 'PUT', url: '/api/usuarios/id-ana' });
    expect(req.request.body).toEqual({ nome: 'Ana Admin', perfil: 'Consulta', ativo: true, versao: 'AAAAAAAAB9E=' });
    responderProblema(req, 422, 'ULTIMO_ADMIN');
    await fixture.whenStable();

    expect(tela().querySelector('[role=alert]')?.textContent?.trim()).toBe('O tenant precisa de ao menos um Admin ativo');
    expect(navegar).not.toHaveBeenCalled();
  });
});
