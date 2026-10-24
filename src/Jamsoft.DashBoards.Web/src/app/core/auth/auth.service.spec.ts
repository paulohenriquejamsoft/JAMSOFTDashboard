import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

const sessaoAdmin = {
  accessToken: 'token-admin',
  expiraEm: '2026-10-24T12:15:00Z',
  perfil: 'Admin',
  tenant: { idPublico: '7c9e6679-7425-40de-944b-e07fc1f90ae7', nome: 'Loja A' },
  trocarSenha: false,
};

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  function entrar(): void {
    auth.login('admin@loja.com', 'senha-forte-1').subscribe();
    http.expectOne('/api/auth/login').flush(sessaoAdmin);
  }

  it('login envia as credenciais e preenche usuario e o access token', () => {
    auth.login('admin@loja.com', 'senha-forte-1').subscribe();

    const req = http.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'admin@loja.com', senha: 'senha-forte-1' });
    req.flush(sessaoAdmin);

    expect(auth.usuario()).toEqual({ perfil: 'Admin', tenant: sessaoAdmin.tenant, trocarSenha: false });
    expect(auth.accessToken).toBe('token-admin');
  });

  it('logout chama a API e limpa usuario e token', () => {
    entrar();

    let concluiu = false;
    auth.logout().subscribe({ complete: () => (concluiu = true) });
    const req = http.expectOne('/api/auth/logout');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(concluiu).toBe(true);
    expect(auth.usuario()).toBeNull();
    expect(auth.accessToken).toBeNull();
  });

  it('3 renovar() simultâneos fazem uma única requisição de refresh', () => {
    const recebidos: string[] = [];
    for (let i = 0; i < 3; i++) auth.renovar().subscribe((u) => recebidos.push(u.perfil));

    const req = http.expectOne('/api/auth/refresh');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.headers.get('X-Requested-With')).toBe('jamsoft');
    req.flush(sessaoAdmin);

    expect(recebidos).toEqual(['Admin', 'Admin', 'Admin']);
    expect(auth.accessToken).toBe('token-admin');
  });

  it('restaurarSessao com refresh 401 deixa usuario = null', async () => {
    const restaurando = auth.restaurarSessao();
    http
      .expectOne('/api/auth/refresh')
      .flush({ codigo: 'NAO_AUTENTICADO' }, { status: 401, statusText: 'Unauthorized' });
    await restaurando;

    expect(auth.usuario()).toBeNull();
    expect(auth.accessToken).toBeNull();
  });

  it('não grava nada em localStorage nem em sessionStorage', async () => {
    const gravacoes = vi.spyOn(Storage.prototype, 'setItem');

    entrar();
    const restaurando = auth.restaurarSessao();
    http.expectOne('/api/auth/refresh').flush({ ...sessaoAdmin, accessToken: 'token-novo' });
    await restaurando;

    expect(auth.accessToken).toBe('token-novo');
    expect(gravacoes).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
