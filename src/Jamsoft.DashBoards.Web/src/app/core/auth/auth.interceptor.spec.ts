import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { TenantContextoService } from './tenant-contexto.service';

const lojaB = { idPublico: '3f2504e0-4f89-11d3-9a0c-0305e82c3301', nome: 'Loja B' };

function sessao(perfil: string, accessToken: string) {
  return {
    accessToken,
    expiraEm: '2026-10-24T12:15:00Z',
    perfil,
    tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
    trocarSenha: false,
  };
}

const naoAutenticado = { status: 401, statusText: 'Unauthorized' };
const problemaNaoAutenticado = { codigo: 'NAO_AUTENTICADO' };

describe('authInterceptor', () => {
  let http: HttpClient;
  let api: HttpTestingController;
  let auth: AuthService;
  let contexto: TenantContextoService;
  let router: Router;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    api = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    contexto = TestBed.inject(TenantContextoService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    api.verify();
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  function entrarComo(perfil: string, token = `token-${perfil}`): void {
    auth.login('u@x.com', 'senha-forte-1').subscribe();
    api.expectOne('/api/auth/login').flush(sessao(perfil, token));
  }

  it('envia Authorization e, para o SuperAdmin com tenant selecionado, X-Tenant', () => {
    entrarComo('SuperAdmin');
    contexto.selecionar(lojaB);

    http.get('/api/usuarios').subscribe();

    const req = api.expectOne('/api/usuarios');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-SuperAdmin');
    expect(req.request.headers.get('X-Tenant')).toBe(lojaB.idPublico);
    req.flush([]);
  });

  it('sem tenant selecionado não envia X-Tenant', () => {
    entrarComo('SuperAdmin');

    http.get('/api/usuarios').subscribe();

    const req = api.expectOne('/api/usuarios');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-SuperAdmin');
    expect(req.request.headers.has('X-Tenant')).toBe(false);
    req.flush([]);
  });

  it('401 renova a sessão e repete a requisição com o token novo', () => {
    entrarComo('Admin', 'token-velho');
    let resposta: unknown;

    http.get('/api/usuarios').subscribe((r) => (resposta = r));
    api.expectOne('/api/usuarios').flush(problemaNaoAutenticado, naoAutenticado);
    api.expectOne('/api/auth/refresh').flush(sessao('Admin', 'token-novo'));
    const repetida = api.expectOne('/api/usuarios');

    expect(repetida.request.headers.get('Authorization')).toBe('Bearer token-novo');
    repetida.flush([{ nome: 'Ana' }]);
    expect(resposta).toEqual([{ nome: 'Ana' }]);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('401 e renovação recusada: encerra a sessão e vai para o login', () => {
    entrarComo('Admin');
    let erro: unknown;

    http.get('/api/usuarios').subscribe({ error: (e) => (erro = e) });
    api.expectOne('/api/usuarios').flush(problemaNaoAutenticado, naoAutenticado);
    api.expectOne('/api/auth/refresh').flush(problemaNaoAutenticado, naoAutenticado);
    api.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });

    expect(erro).toMatchObject({ status: 401 });
    expect(auth.usuario()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/conta/login');
  });

  it('401 de novo na requisição repetida: vai para o login sem renovar outra vez', () => {
    entrarComo('Admin');
    let erro: unknown;

    http.get('/api/usuarios').subscribe({ error: (e) => (erro = e) });
    api.expectOne('/api/usuarios').flush(problemaNaoAutenticado, naoAutenticado);
    api.expectOne('/api/auth/refresh').flush(sessao('Admin', 'token-novo'));
    api.expectOne('/api/usuarios').flush(problemaNaoAutenticado, naoAutenticado);
    api.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });

    expect(erro).toMatchObject({ status: 401 });
    expect(auth.usuario()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/conta/login');
  });

  it('401 em /api/auth/* não dispara renovação', () => {
    let erro: unknown;

    http.post('/api/auth/login', {}).subscribe({ error: (e) => (erro = e) });
    api
      .expectOne('/api/auth/login')
      .flush({ codigo: 'CREDENCIAIS_INVALIDAS' }, naoAutenticado);

    api.expectNone('/api/auth/refresh');
    expect(erro).toMatchObject({ status: 401 });
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('403 TROCA_SENHA_PENDENTE redireciona para a troca de senha', () => {
    entrarComo('Admin');
    let erro: unknown;

    http.get('/api/usuarios').subscribe({ error: (e) => (erro = e) });
    api
      .expectOne('/api/usuarios')
      .flush({ codigo: 'TROCA_SENHA_PENDENTE' }, { status: 403, statusText: 'Forbidden' });

    expect(erro).toMatchObject({ status: 403 });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/conta/trocar-senha');
    api.expectNone('/api/auth/refresh');
  });
});
