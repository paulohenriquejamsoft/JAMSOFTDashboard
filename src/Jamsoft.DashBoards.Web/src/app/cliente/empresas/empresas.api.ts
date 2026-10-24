import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Empresa como sai da API (`EmpresaResposta`); `nomeManual` = nome editado, a ingestão não sobrescreve. */
export interface Empresa {
  idPublico: string;
  codEmpresa: number;
  nome: string;
  nomeManual: boolean;
}

const BASE = '/api/empresas';

/** Chamadas de `/api/empresas` (EMP-02, EMP-03, EMP-05, EMP-06) no tenant efetivo. */
@Injectable({ providedIn: 'root' })
export class EmpresasApi {
  private readonly http = inject(HttpClient);

  /** Lista do tenant, ordenada pelo código. */
  listar(): Observable<Empresa[]> {
    return this.http.get<Empresa[]>(BASE);
  }

  criar(codEmpresa: number, nome: string): Observable<Empresa> {
    return this.http.post<Empresa>(BASE, { codEmpresa, nome });
  }

  renomear(idPublico: string, nome: string): Observable<Empresa> {
    return this.http.put<Empresa>(`${BASE}/${idPublico}`, { nome });
  }
}
