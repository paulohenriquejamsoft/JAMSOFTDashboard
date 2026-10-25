import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, expand, reduce } from 'rxjs';
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

export interface CriarColetorRequisicao {
  nome: string;
  tenants: string[];
}

export interface AlterarTenantsRequisicao {
  tenants: string[];
  versao: string;
}

/** Maior página aceita pela API (`TenantsEndpoints.TamanhoMaximo`). */
export const TAMANHO_MAXIMO = 100;

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

  /** Todos os coletores, página a página: a API não tem leitura por ID. */
  listarTodos(): Observable<Coletor[]> {
    return todasAsPaginas((pagina) => this.listar(pagina, TAMANHO_MAXIMO));
  }

  criar(corpo: CriarColetorRequisicao): Observable<CredencialColetor> {
    return this.http.post<CredencialColetor>(BASE, corpo);
  }

  alterarTenants(idPublico: string, corpo: AlterarTenantsRequisicao): Observable<Coletor> {
    return this.http.put<Coletor>(`${BASE}/${idPublico}/tenants`, corpo);
  }

  revogar(idPublico: string): Observable<Coletor> {
    return this.http.post<Coletor>(`${BASE}/${idPublico}/revogar`, null);
  }

  rotacionar(idPublico: string): Observable<CredencialColetor> {
    return this.http.post<CredencialColetor>(`${BASE}/${idPublico}/rotacionar`, null);
  }
}

/** Busca a página 1 e as seguintes enquanto houver itens, juntando tudo numa lista. */
export function todasAsPaginas<T>(buscar: (pagina: number) => Observable<Paginado<T>>): Observable<T[]> {
  return buscar(1).pipe(
    expand((p) => (p.pagina * p.tamanho < p.total ? buscar(p.pagina + 1) : EMPTY)),
    reduce((itens, p) => [...itens, ...p.itens], [] as T[]),
  );
}
