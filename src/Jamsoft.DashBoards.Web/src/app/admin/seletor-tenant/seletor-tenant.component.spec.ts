import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService, Perfil, UsuarioSessao } from '../../core/auth/auth.service';
import { AVISO_SELECIONE_TENANT } from '../../core/auth/perfil.guard';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';
import { Tenant } from '../tenants/tenants.api';
import { DEBOUNCE_SELETOR_MS, SeletorTenantComponent } from './seletor-tenant.component';

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

describe('SeletorTenantComponent', () => {
  let fixture: ComponentFixture<SeletorTenantComponent>;
  let http: HttpTestingController;

  async function criar(perfil: Perfil): Promise<void> {
    const sessao: UsuarioSessao = {
      perfil,
      tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
      trocarSenha: false,
    };
    TestBed.configureTestingModule({
      imports: [SeletorTenantComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { usuario: signal(sessao).asReadonly() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SeletorTenantComponent);
    await renderizar();
  }

  beforeEach(() => {
    sessionStorage.clear();
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    sessionStorage.clear();
  });

  const tela = () => fixture.nativeElement as HTMLElement;
  const botao = (texto: string) =>
    [...tela().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().startsWith(texto));
  const busca = (termo: string) => (r: { urlWithParams: string }) =>
    r.urlWithParams === `/api/admin/tenants?pagina=1${termo ? `&busca=${termo}` : ''}&tamanho=10`;

  async function renderizar(ms = 0): Promise<void> {
    await vi.advanceTimersByTimeAsync(ms);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
  }

  it.each<Perfil>(['Admin', 'Consulta'])('fica invisível para %s', async (perfil) => {
    await criar(perfil);

    expect(tela().textContent?.trim()).toBe('');
    expect(tela().querySelector('button')).toBeNull();
  });

  it('abrir lista tenants e digitar busca consulta com o termo', async () => {
    await criar('SuperAdmin');
    botao('Selecionar tenant')!.click();
    await renderizar(DEBOUNCE_SELETOR_MS);
    http.expectOne(busca('')).flush({ itens: [tenant('Loja A'), tenant('Loja B', false)], pagina: 1, tamanho: 10, total: 2 });
    await renderizar();
    expect([...tela().querySelectorAll('li button')].map((b) => b.textContent?.trim())).toEqual([
      'Loja A',
      'Loja B (inativo)',
    ]);

    const campo = tela().querySelector<HTMLInputElement>('#seletor-tenant-busca')!;
    campo.value = 'Loja B';
    campo.dispatchEvent(new Event('input'));
    await renderizar(DEBOUNCE_SELETOR_MS);
    http.expectOne(busca('Loja%20B')).flush({ itens: [tenant('Loja B', false)], pagina: 1, tamanho: 10, total: 1 });
    await renderizar();

    expect([...tela().querySelectorAll('li button')].map((b) => b.textContent?.trim())).toEqual(['Loja B (inativo)']);
  });

  it('escolher grava o contexto, fecha a busca e mostra o nome', async () => {
    await criar('SuperAdmin');
    botao('Selecionar tenant')!.click();
    await renderizar(DEBOUNCE_SELETOR_MS);
    http.expectOne(busca('')).flush({ itens: [tenant('Loja B', false)], pagina: 1, tamanho: 10, total: 1 });
    await renderizar();

    tela().querySelector<HTMLButtonElement>('li button')!.click();
    await renderizar();

    expect(TestBed.inject(TenantContextoService).selecionado()).toEqual({ idPublico: 'id-loja-b', nome: 'Loja B' });
    expect(tela().querySelector('[data-teste=tenant-atual]')?.textContent).toBe('Loja B');
    expect(tela().querySelector('#seletor-tenant-painel')).toBeNull();
  });

  it('Limpar remove a seleção', async () => {
    await criar('SuperAdmin');
    const contexto = TestBed.inject(TenantContextoService);
    contexto.selecionar({ idPublico: 'id-loja-b', nome: 'Loja B' });
    await renderizar();
    expect(tela().querySelector('[data-teste=tenant-atual]')?.textContent).toBe('Loja B');

    botao('Limpar')!.click();
    await renderizar();

    expect(contexto.selecionado()).toBeNull();
    expect(tela().querySelector('[data-teste=tenant-atual]')).toBeNull();
    expect(tela().textContent).toContain('Nenhum selecionado');
  });

  it('abre sozinho quando o guard redireciona com "Selecione um tenant"', async () => {
    await criar('SuperAdmin');

    await TestBed.inject(Router).navigateByUrl('/admin', { state: { aviso: AVISO_SELECIONE_TENANT } });
    await renderizar(DEBOUNCE_SELETOR_MS);

    expect(tela().querySelector('#seletor-tenant-painel')).not.toBeNull();
    http.expectOne(busca('')).flush({ itens: [], pagina: 1, tamanho: 10, total: 0 });
  });
});
