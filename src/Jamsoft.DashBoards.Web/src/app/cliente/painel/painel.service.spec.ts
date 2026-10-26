import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { AuthService } from '../../core/auth/auth.service';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';
import { FiltroPainel, PainelService } from './painel.service';

const EMPRESA_1 = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const EMPRESA_2 = '9a7b1c2d-0000-4000-8000-000000000002';
const outubro: FiltroPainel = { inicio: '2026-10-01', fim: '2026-10-24', empresas: [], tipos: [], vendedores: [] };

function sessao(perfil: string, accessToken: string) {
  return {
    accessToken,
    expiraEm: '2026-10-24T12:15:00Z',
    perfil,
    tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
    trocarSenha: false,
  };
}

describe('PainelService', () => {
  let servico: PainelService;
  let api: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    servico = TestBed.inject(PainelService);
    api = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    api.verify();
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  function entrarComo(perfil: string, token: string): void {
    auth.login('u@x.com', 'senha-forte-1').subscribe();
    api.expectOne('/api/auth/login').flush(sessao(perfil, token));
  }

  it('kpis chama /api/painel/kpis com inicio e fim, sem empresas quando a lista é vazia', () => {
    let resposta: unknown;
    servico.kpis(outubro).subscribe((r) => (resposta = r));

    const req = api.expectOne((r) => r.url === '/api/painel/kpis');
    expect(req.request.method).toBe('GET');
    expect(req.request.urlWithParams).toBe('/api/painel/kpis?inicio=2026-10-01&fim=2026-10-24');
    expect(req.request.params.has('empresas')).toBe(false);
    req.flush({ semVendas: true });
    expect(resposta).toEqual({ semVendas: true });
  });

  it('evolucao chama /api/painel/evolucao com uma empresa', () => {
    servico.evolucao({ ...outubro, empresas: [EMPRESA_1] }).subscribe();

    const req = api.expectOne((r) => r.url === '/api/painel/evolucao');
    expect(req.request.urlWithParams).toBe(
      `/api/painel/evolucao?inicio=2026-10-01&fim=2026-10-24&empresas=${EMPRESA_1}`,
    );
    req.flush({ granularidade: 'dia', pontos: [] });
  });

  it('rankings repete empresas uma vez por empresa selecionada', () => {
    servico.rankings({ ...outubro, empresas: [EMPRESA_1, EMPRESA_2] }).subscribe();

    const req = api.expectOne((r) => r.url === '/api/painel/rankings');
    expect(req.request.params.getAll('empresas')).toEqual([EMPRESA_1, EMPRESA_2]);
    expect(req.request.urlWithParams).toBe(
      `/api/painel/rankings?inicio=2026-10-01&fim=2026-10-24&empresas=${EMPRESA_1}&empresas=${EMPRESA_2}`,
    );
    req.flush({ totalFaturamento: 0, vendedores: [], clientes: [], empresas: [] });
  });

  it('dfe chama /api/painel/dfe com o filtro', () => {
    servico.dfe(outubro).subscribe();

    const req = api.expectOne((r) => r.url === '/api/painel/dfe');
    expect(req.request.urlWithParams).toBe('/api/painel/dfe?inicio=2026-10-01&fim=2026-10-24');
    req.flush({ tipos: [] });
  });

  it('dfe repete tipos e vendedores uma vez por item (DASH-10)', () => {
    servico.dfe({ ...outubro, tipos: ['NF-e', 'Pedido'], vendedores: [7] }).subscribe();

    const req = api.expectOne((r) => r.url === '/api/painel/dfe');
    expect(req.request.urlWithParams).toBe(
      '/api/painel/dfe?inicio=2026-10-01&fim=2026-10-24&tipos=NF-e&tipos=Pedido&vendedores=7',
    );
    req.flush({ tipos: [] });
  });

  it('vendedores chama /api/painel/vendedores', () => {
    let resposta: unknown;
    servico.vendedores().subscribe((r) => (resposta = r));

    api.expectOne('/api/painel/vendedores').flush([{ codigo: 1, nome: 'Ana' }]);
    expect(resposta).toEqual([{ codigo: 1, nome: 'Ana' }]);
  });

  it('401 em /api/painel/kpis renova a sessão e repete com o token novo', () => {
    entrarComo('Admin', 'token-velho');
    let resposta: unknown;

    servico.kpis(outubro).subscribe((r) => (resposta = r));
    const primeira = api.expectOne((r) => r.url === '/api/painel/kpis');
    expect(primeira.request.headers.get('Authorization')).toBe('Bearer token-velho');
    primeira.flush({ codigo: 'NAO_AUTENTICADO' }, { status: 401, statusText: 'Unauthorized' });
    api.expectOne('/api/auth/refresh').flush(sessao('Admin', 'token-novo'));

    const repetida = api.expectOne((r) => r.url === '/api/painel/kpis');
    expect(repetida.request.headers.get('Authorization')).toBe('Bearer token-novo');
    expect(repetida.request.urlWithParams).toBe('/api/painel/kpis?inicio=2026-10-01&fim=2026-10-24');
    repetida.flush({ semVendas: false });
    expect(resposta).toEqual({ semVendas: false });
  });

  it('SuperAdmin com tenant selecionado envia X-Tenant', () => {
    entrarComo('SuperAdmin', 'token-sa');
    TestBed.inject(TenantContextoService).selecionar({ idPublico: 'b2', nome: 'Loja B' });

    servico.dfe(outubro).subscribe();

    const req = api.expectOne((r) => r.url === '/api/painel/dfe');
    expect(req.request.headers.get('X-Tenant')).toBe('b2');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-sa');
    req.flush({ tipos: [] });
  });
});
