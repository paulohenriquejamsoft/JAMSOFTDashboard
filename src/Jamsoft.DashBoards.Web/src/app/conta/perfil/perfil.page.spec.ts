import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { PerfilPage } from './perfil.page';

const EU_ADMIN = {
  idPublico: '0b7a1d2e-0000-4000-8000-000000000001',
  nome: 'Maria Souza',
  email: 'maria@loja-a.com.br',
  perfil: 'Admin',
  tenant: { idPublico: 'a1', nome: 'Loja A' },
  trocarSenha: false,
};

describe('PerfilPage', () => {
  let fixture: ComponentFixture<PerfilPage>;
  let http: HttpTestingController;

  async function criar(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [PerfilPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PerfilPage);
    await fixture.whenStable();
  }

  async function responder(corpo: object): Promise<void> {
    const req = http.expectOne('/api/auth/eu');
    expect(req.request.method).toBe('GET');
    req.flush(corpo);
    await fixture.whenStable();
  }

  /** Valor exibido para cada rótulo da lista de dados (`dt` → `dd`). */
  function dados(): Record<string, string> {
    const el = fixture.nativeElement as HTMLElement;
    const resultado: Record<string, string> = {};
    el.querySelectorAll('dt').forEach((dt) => {
      resultado[dt.textContent!.trim()] = (dt.nextElementSibling as HTMLElement).textContent!.trim();
    });
    return resultado;
  }

  afterEach(() => http.verify());

  it('mostra nome, e-mail, perfil e tenant vindos de /api/auth/eu (UI-01)', async () => {
    await criar();
    await responder(EU_ADMIN);

    expect(dados()).toEqual({
      Nome: 'Maria Souza',
      'E-mail': 'maria@loja-a.com.br',
      Perfil: 'Administrador',
      Tenant: 'Loja A',
    });
  });

  it('tem o link para trocar a senha', async () => {
    await criar();
    await responder(EU_ADMIN);

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/conta/trocar-senha"]') as HTMLElement;
    expect(link.textContent!.trim()).toBe('Trocar senha');
  });

  it('SuperAdmin (sem tenant próprio) aparece com perfil SuperAdmin e tenant "—"', async () => {
    await criar();
    await responder({ ...EU_ADMIN, nome: 'Root', email: 'root@jamsoft.com.br', perfil: 'SuperAdmin', tenant: null });

    expect(dados()).toEqual({ Nome: 'Root', 'E-mail': 'root@jamsoft.com.br', Perfil: 'SuperAdmin', Tenant: '—' });
  });

  it('falha ao carregar mostra o erro e "Tentar novamente" refaz a consulta (UI-08)', async () => {
    await criar();
    responderProblema(http.expectOne('/api/auth/eu'), 500, 'ERRO_INTERNO');
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[role="alert"]')).not.toBeNull();
    expect(el.querySelector('dl')).toBeNull();

    (el.querySelector('[role="alert"] button') as HTMLButtonElement).click();
    await responder(EU_ADMIN);
    expect(dados()['Nome']).toBe('Maria Souza');
  });
});
