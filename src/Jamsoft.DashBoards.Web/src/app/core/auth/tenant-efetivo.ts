import { Signal, computed, inject } from '@angular/core';
import { AuthService } from './auth.service';
import { TenantContextoService } from './tenant-contexto.service';

/**
 * ID público do tenant em que as telas do cliente operam: o do token para Admin/Consulta, o selecionado para o
 * SuperAdmin (CTX-01). `null` quando o SuperAdmin ainda não escolheu. Chamar num contexto de injeção.
 */
export function tenantEfetivo(): Signal<string | null> {
  const auth = inject(AuthService);
  const contexto = inject(TenantContextoService);
  return computed(() => {
    const usuario = auth.usuario();
    if (!usuario) return null;
    return usuario.perfil === 'SuperAdmin'
      ? (contexto.selecionado()?.idPublico ?? null)
      : (usuario.tenant?.idPublico ?? null);
  });
}
