import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { routes } from '../../app.routes';
import { AuthService, Perfil, UsuarioSessao } from './auth.service';
import { TenantContextoService } from './tenant-contexto.service';

type Ator = 'SuperAdmin sem tenant' | 'SuperAdmin com tenant' | 'Admin' | 'Consulta' | 'anônimo';

interface Esperado {
  url: string;
  aviso?: string;
}

const NEGADO = 'Acesso não permitido';
const SEM_TENANT = 'Selecione um tenant';
const liberado = (url: string): Esperado => ({ url });
const login: Esperado = { url: '/conta/login' };

/** Matriz perfil × rota com o resultado esperado de cada combinação. */
const matriz: [Ator, string, Esperado][] = [
  ['SuperAdmin sem tenant', '/admin', liberado('/admin')],
  ['SuperAdmin sem tenant', '/cliente', { url: '/admin', aviso: SEM_TENANT }],
  ['SuperAdmin sem tenant', '/cliente/usuarios', { url: '/admin', aviso: SEM_TENANT }],
  ['SuperAdmin sem tenant', '/cliente/empresas', { url: '/admin', aviso: SEM_TENANT }],

  ['SuperAdmin com tenant', '/admin', liberado('/admin')],
  ['SuperAdmin com tenant', '/cliente', liberado('/cliente')],
  ['SuperAdmin com tenant', '/cliente/usuarios', liberado('/cliente/usuarios')],
  ['SuperAdmin com tenant', '/cliente/empresas', liberado('/cliente/empresas')],

  ['Admin', '/admin', { url: '/cliente', aviso: NEGADO }],
  ['Admin', '/cliente', liberado('/cliente')],
  ['Admin', '/cliente/usuarios', liberado('/cliente/usuarios')],
  ['Admin', '/cliente/empresas', liberado('/cliente/empresas')],

  ['Consulta', '/admin', { url: '/cliente', aviso: NEGADO }],
  ['Consulta', '/cliente', liberado('/cliente')],
  ['Consulta', '/cliente/usuarios', { url: '/cliente', aviso: NEGADO }],
  ['Consulta', '/cliente/empresas', { url: '/cliente', aviso: NEGADO }],

  ['anônimo', '/admin', login],
  ['anônimo', '/cliente', login],
  ['anônimo', '/cliente/usuarios', login],
  ['anônimo', '/cliente/empresas', login],
  ['anônimo', '/conta/login', login],

  // Gestão de Coletores: só SuperAdmin (COL-08).
  ['SuperAdmin sem tenant', '/admin/coletores', liberado('/admin/coletores')],
  ['Admin', '/admin/coletores', { url: '/cliente', aviso: NEGADO }],
  ['Consulta', '/admin/coletores', { url: '/cliente', aviso: NEGADO }],
  ['anônimo', '/admin/coletores', login],

  // Sincronização do tenant: Admin ou SuperAdmin com tenant (STS-01).
  ['SuperAdmin sem tenant', '/cliente/sincronizacao', { url: '/admin', aviso: SEM_TENANT }],
  ['SuperAdmin com tenant', '/cliente/sincronizacao', liberado('/cliente/sincronizacao')],
  ['Admin', '/cliente/sincronizacao', liberado('/cliente/sincronizacao')],
  ['Consulta', '/cliente/sincronizacao', { url: '/cliente', aviso: NEGADO }],
];

function sessaoDe(ator: Ator): UsuarioSessao | null {
  if (ator === 'anônimo') return null;
  const perfil: Perfil = ator.startsWith('SuperAdmin') ? 'SuperAdmin' : (ator as Perfil);
  return {
    perfil,
    tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
    trocarSenha: false,
  };
}

describe('perfilGuard + rotas por perfil', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => sessionStorage.clear());

  it.each(matriz)('%s em %s', async (ator, rota, esperado) => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        { provide: AuthService, useValue: { usuario: signal(sessaoDe(ator)).asReadonly() } },
      ],
    });
    if (ator === 'SuperAdmin com tenant') {
      TestBed.inject(TenantContextoService).selecionar({ idPublico: 'b2', nome: 'Loja B' });
    }
    const router = TestBed.inject(Router);

    await router.navigateByUrl(rota);

    expect(router.url).toBe(esperado.url);
    const estado = router.lastSuccessfulNavigation()?.extras.state as { aviso?: string } | undefined;
    expect(estado?.aviso).toBe(esperado.aviso);
  });
});
