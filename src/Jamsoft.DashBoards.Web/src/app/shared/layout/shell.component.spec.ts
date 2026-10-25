import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService, Perfil, UsuarioSessao } from '../../core/auth/auth.service';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';
import { ShellComponent } from './shell.component';

@Component({ template: '' })
class Vazio {}

type Ator = 'Consulta' | 'Admin' | 'SuperAdmin sem tenant' | 'SuperAdmin com tenant';

function sessaoDe(ator: Ator): UsuarioSessao {
  const perfil: Perfil = ator.startsWith('SuperAdmin') ? 'SuperAdmin' : (ator as Perfil);
  return {
    perfil,
    tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
    trocarSenha: false,
  };
}

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;
  let http: HttpTestingController;

  async function criar(ator: Ator): Promise<void> {
    TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideRouter([{ path: '**', component: Vazio }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { usuario: signal(sessaoDe(ator)).asReadonly() } },
      ],
    });
    if (ator === 'SuperAdmin com tenant') {
      TestBed.inject(TenantContextoService).selecionar({ idPublico: 'b2', nome: 'Loja B' });
    }
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    // O status dos dados do cabeçalho consulta o resumo quando há tenant; não interessa aqui.
    http.match('/api/sincronizacao/resumo').forEach((r) => r.flush({ ultimoSucessoEm: null }));
  }

  function itensMenu(): { rotulo: string; link: string | null }[] {
    const links = fixture.nativeElement.querySelectorAll('nav[aria-label="Menu principal"] a') as NodeListOf<HTMLAnchorElement>;
    return [...links].map((a) => ({ rotulo: a.textContent!.trim(), link: a.getAttribute('href') }));
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('Consulta vê só Painel e Meu perfil (UI-01)', async () => {
    await criar('Consulta');

    expect(itensMenu()).toEqual([
      { rotulo: 'Painel', link: '/cliente' },
      { rotulo: 'Meu perfil', link: '/conta/perfil' },
    ]);
  });

  it('Admin vê Painel, Usuários, Empresas, Sincronização e Meu perfil (UI-02)', async () => {
    await criar('Admin');

    expect(itensMenu()).toEqual([
      { rotulo: 'Painel', link: '/cliente' },
      { rotulo: 'Usuários', link: '/cliente/usuarios' },
      { rotulo: 'Empresas', link: '/cliente/empresas' },
      { rotulo: 'Sincronização', link: '/cliente/sincronizacao' },
      { rotulo: 'Meu perfil', link: '/conta/perfil' },
    ]);
  });

  it('SuperAdmin sem tenant vê só a área administrativa e o seletor de tenant (UI-03)', async () => {
    await criar('SuperAdmin sem tenant');

    expect(itensMenu()).toEqual([
      { rotulo: 'Tenants', link: '/admin/tenants' },
      { rotulo: 'Coletores', link: '/admin/coletores' },
      { rotulo: 'Auditoria', link: '/admin/auditoria' },
    ]);
    const seletor = fixture.nativeElement.querySelector('app-seletor-tenant') as HTMLElement;
    expect(seletor.textContent).toContain('Selecionar tenant');
  });

  it('SuperAdmin com tenant vê a área administrativa e as telas do tenant (UI-03)', async () => {
    await criar('SuperAdmin com tenant');

    expect(itensMenu()).toEqual([
      { rotulo: 'Tenants', link: '/admin/tenants' },
      { rotulo: 'Coletores', link: '/admin/coletores' },
      { rotulo: 'Auditoria', link: '/admin/auditoria' },
      { rotulo: 'Painel', link: '/cliente' },
      { rotulo: 'Usuários', link: '/cliente/usuarios' },
      { rotulo: 'Empresas', link: '/cliente/empresas' },
      { rotulo: 'Sincronização', link: '/cliente/sincronizacao' },
    ]);
  });

  it('mostra o aviso "Acesso não permitido" deixado pelo guard ao redirecionar (UI-04)', async () => {
    await criar('Consulta');

    await TestBed.inject(Router).navigateByUrl('/cliente', { state: { aviso: 'Acesso não permitido' } });
    fixture.detectChanges();
    await fixture.whenStable();

    const alerta = fixture.nativeElement.querySelector('main .alerta') as HTMLElement;
    expect(alerta.textContent!.trim()).toBe('Acesso não permitido');
  });
});
