import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { TenantResumo } from '../../core/auth/auth.service';
import { Paginado } from '../../shared/paginado';

/** Coletor como sai da API (`ColetorResposta`): sem secret. `versao` é o ROWVERSION em Base64. */
export interface Coletor {
  idPublico: string;
  nome: string;
  clientId: string;
  ativo: boolean;
  tenants: TenantResumo[];
  criadoEm: string;
  revogadoEm: string | null;
  ultimoTokenEm: string | null;
  versao: string;
}

/** Resposta de criar e rotacionar: o `clientSecret` só aparece aqui (COL-01, COL-06). */
export interface CredencialColetor {
  idPublico: string;
  clientId: string;
  clientSecret: string;
}

const BASE = '/api/admin/coletores';

/** Chamadas de `/api/admin/coletores` (COL-01, COL-05..COL-08), somente SuperAdmin. */
@Injectable({ providedIn: 'root' })
export class ColetoresApi {
  private readonly http = inject(HttpClient);

  listar(pagina: number, tamanho?: number): Observable<Paginado<Coletor>> {
    let params = new HttpParams().set('pagina', pagina);
    if (tamanho) params = params.set('tamanho', tamanho);
    return this.http.get<Paginado<Coletor>>(BASE, { params });
  }

  revogar(idPublico: string): Observable<Coletor> {
    return this.http.post<Coletor>(`${BASE}/${idPublico}/revogar`, null);
  }

  rotacionar(idPublico: string): Observable<CredencialColetor> {
    return this.http.post<CredencialColetor>(`${BASE}/${idPublico}/rotacionar`, null);
  }
}
