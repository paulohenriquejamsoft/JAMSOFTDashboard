import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { TenantContextoService } from './tenant-contexto.service';

const lojaB = { idPublico: '3f2504e0-4f89-11d3-9a0c-0305e82c3301', nome: 'Loja B' };

describe('TenantContextoService', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  /** Simula o recarregamento da página: injetor novo, mesmo sessionStorage. */
  function recarregar(): TenantContextoService {
    TestBed.resetTestingModule();
    return TestBed.inject(TenantContextoService);
  }

  it('selecionar guarda o tenant e limpar remove', () => {
    const contexto = TestBed.inject(TenantContextoService);

    contexto.selecionar(lojaB);
    expect(contexto.selecionado()).toEqual(lojaB);
    expect(recarregar().selecionado()).toEqual(lojaB);

    const depois = TestBed.inject(TenantContextoService);
    depois.limpar();
    expect(depois.selecionado()).toBeNull();
    expect(recarregar().selecionado()).toBeNull();
  });

  it('restaura o tenant selecionado após recarregar a página', () => {
    TestBed.inject(TenantContextoService).selecionar(lojaB);

    const recarregado = recarregar();

    expect(recarregado.selecionado()).toEqual(lojaB);
  });

  it('sessionStorage indisponível não quebra: a seleção vale em memória', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('bloqueado', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('bloqueado', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new DOMException('bloqueado', 'SecurityError');
    });

    const contexto = TestBed.inject(TenantContextoService);
    expect(contexto.selecionado()).toBeNull();

    contexto.selecionar(lojaB);
    expect(contexto.selecionado()).toEqual(lojaB);

    contexto.limpar();
    expect(contexto.selecionado()).toBeNull();
  });

  it('logout limpa o tenant selecionado', () => {
    const contexto = TestBed.inject(TenantContextoService);
    const auth = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    contexto.selecionar(lojaB);

    auth.logout().subscribe();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });

    expect(contexto.selecionado()).toBeNull();
    expect(recarregar().selecionado()).toBeNull();
    http.verify();
  });
});
