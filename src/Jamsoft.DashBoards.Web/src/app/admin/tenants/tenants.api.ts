import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Paginado } from '../../shared/paginado';

/** Tenant como sai da API (`TenantResposta`); `versao` é o ROWVERSION em Base64. */
export interface Tenant {
  idPublico: string;
  nome: string;
  cnpj: string;
  ativo: boolean;
  criadoEm: string;
  alteradoEm: string | null;
  versao: string;
}

export type SituacaoFiltro = '' | 'Ativo' | 'Inativo';

export interface ConsultaTenants {
  busca: string;
  situacao: SituacaoFiltro;
  pagina: number;
  tamanho?: number;
}

const BASE = '/api/admin/tenants';

/** Chamadas de `/api/admin/tenants` (TEN-01..TEN-06), somente SuperAdmin. */
@Injectable({ providedIn: 'root' })
export class TenantsApi {
  private readonly http = inject(HttpClient);

  listar(consulta: ConsultaTenants): Observable<Paginado<Tenant>> {
    let params = new HttpParams().set('pagina', consulta.pagina);
    if (consulta.busca.trim()) params = params.set('busca', consulta.busca.trim());
    if (consulta.situacao) params = params.set('situacao', consulta.situacao);
    if (consulta.tamanho) params = params.set('tamanho', consulta.tamanho);
    return this.http.get<Paginado<Tenant>>(BASE, { params });
  }

  inativar(idPublico: string): Observable<Tenant> {
    return this.http.post<Tenant>(`${BASE}/${idPublico}/inativar`, null);
  }

  reativar(idPublico: string): Observable<Tenant> {
    return this.http.post<Tenant>(`${BASE}/${idPublico}/reativar`, null);
  }
}

/** 14 dígitos → 00.000.000/0000-00; outro tamanho volta como veio. */
export function formatarCnpj(valor: string): string {
  const d = valor.replace(/\D/g, '');
  return d.length === 14 ? `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}` : valor;
}
