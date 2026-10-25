import { inject } from '@angular/core';
import { CanMatchFn, RedirectCommand, Router } from '@angular/router';
import { AuthService, Perfil } from './auth.service';
import { TenantContextoService } from './tenant-contexto.service';

export const AVISO_ACESSO_NEGADO = 'Acesso não permitido';
export const AVISO_SELECIONE_TENANT = 'Selecione um tenant';

/** Quem pode entrar numa área. O SuperAdmin pode precisar de um tenant selecionado (CTX-01). */
export interface RegraAcesso {
  perfis: readonly Perfil[];
  superAdminExigeTenant?: boolean;
}

/** Estado da navegação de redirecionamento: a home mostra o aviso. */
export interface EstadoAviso {
  aviso: string;
}

export const SO_SUPERADMIN: RegraAcesso = { perfis: ['SuperAdmin'] };
/** Leitura do tenant: Consulta, Admin ou SuperAdmin com tenant (policy LeituraTenant da API). */
export const LEITURA_TENANT: RegraAcesso = {
  perfis: ['SuperAdmin', 'Admin', 'Consulta'],
  superAdminExigeTenant: true,
};
/** Qualquer usuário logado (ex.: "Meu perfil"). */
export const QUALQUER_PERFIL: RegraAcesso = { perfis: ['SuperAdmin', 'Admin', 'Consulta'] };
/** Gestão do tenant: Admin ou SuperAdmin com tenant (policy AdminTenant da API). */
export const ADMIN_TENANT: RegraAcesso = { perfis: ['SuperAdmin', 'Admin'], superAdminExigeTenant: true };

const HOME: Record<Perfil, string> = {
  SuperAdmin: '/admin',
  Admin: '/cliente',
  Consulta: '/cliente',
};

/**
 * `canMatch` por perfil (AUTH-08, TEN-08, USR-10). Sem sessão → login; sem permissão → home do perfil
 * com aviso. É só navegação: a API revalida tudo (AD-008).
 */
export function perfilGuard(regra: RegraAcesso): CanMatchFn {
  return () => {
    const router = inject(Router);
    const usuario = inject(AuthService).usuario();
    if (!usuario) return router.parseUrl('/conta/login');

    let aviso: string | null = null;
    if (!regra.perfis.includes(usuario.perfil)) aviso = AVISO_ACESSO_NEGADO;
    else if (
      usuario.perfil === 'SuperAdmin' &&
      regra.superAdminExigeTenant &&
      !inject(TenantContextoService).selecionado()
    )
      aviso = AVISO_SELECIONE_TENANT;

    if (!aviso) return true;
    const state: EstadoAviso = { aviso };
    return new RedirectCommand(router.parseUrl(HOME[usuario.perfil]), { state: { ...state } });
  };
}
