import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Paginado } from '../../shared/paginado';

/** Registro como sai da API (`RegistroAuditoriaResposta`); `ocorridoEm` em UTC, `detalhes` é JSON sem segredos. */
export interface RegistroAuditoria {
  id: number;
  ocorridoEm: string;
  usuarioIdPublico: string | null;
  usuarioEmail: string | null;
  perfil: string | null;
  operacao: string;
  recursoIdPublico: string | null;
  tenantIdPublico: string | null;
  tenantNome: string | null;
  ip: string | null;
  resultado: string;
  detalhes: string | null;
}

/** Filtros da tela; datas `de`/`ate` no formato `yyyy-MM-dd` do `<input type="date">`. */
export interface FiltrosAuditoria {
  de: string;
  ate: string;
  tenant: string;
  usuario: string;
  operacao: string;
}

/** `GET /api/admin/auditoria` (AUD-03), somente SuperAdmin. */
@Injectable({ providedIn: 'root' })
export class AuditoriaApi {
  private readonly http = inject(HttpClient);

  consultar(filtros: FiltrosAuditoria, pagina: number): Observable<Paginado<RegistroAuditoria>> {
    let params = new HttpParams().set('pagina', pagina);
    // Período inclusivo no fuso do navegador: do início do dia "de" ao fim do dia "até".
    if (filtros.de) params = params.set('de', new Date(`${filtros.de}T00:00:00`).toISOString());
    if (filtros.ate) params = params.set('ate', new Date(`${filtros.ate}T23:59:59.999`).toISOString());
    if (filtros.tenant.trim()) params = params.set('tenant', filtros.tenant.trim());
    if (filtros.usuario.trim()) params = params.set('usuario', filtros.usuario.trim());
    if (filtros.operacao) params = params.set('operacao', filtros.operacao);
    return this.http.get<Paginado<RegistroAuditoria>>('/api/admin/auditoria', { params });
  }
}
