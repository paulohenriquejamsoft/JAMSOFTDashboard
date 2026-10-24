import type { Perfil } from './auth.service';

/** Primeira tela de cada perfil depois do login ou da troca de senha. */
export function homeDoPerfil(perfil: Perfil): string {
  return perfil === 'SuperAdmin' ? '/admin/tenants' : '/cliente';
}
