import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * Api-key do servidor MCP como sai da API (`ChaveMcpResposta`): sem a chave. `expiraEm` é o último dia válido
 * (yyyy-MM-dd, inclusive, em São Paulo); nulo = não expira.
 */
export interface ChaveMcp {
  idPublico: string;
  prefixo: string;
  descricao: string;
  ativo: boolean;
  criadoEm: string;
  expiraEm: string | null;
  revogadoEm: string | null;
}

/** Resposta da criação: `chave` só aparece aqui. */
export interface ChaveMcpCriada {
  chave: string;
  registro: ChaveMcp;
}

const BASE = '/api/chaves-mcp';

/** Chamadas de `/api/chaves-mcp` no tenant efetivo (Admin, ou SuperAdmin com tenant selecionado). */
@Injectable({ providedIn: 'root' })
export class ChavesMcpApi {
  private readonly http = inject(HttpClient);

  /** Ativas primeiro, depois as mais novas. */
  listar(): Observable<ChaveMcp[]> {
    return this.http.get<ChaveMcp[]>(BASE);
  }

  criar(descricao: string, expiraEm: string | null): Observable<ChaveMcpCriada> {
    return this.http.post<ChaveMcpCriada>(BASE, { descricao, expiraEm });
  }

  revogar(idPublico: string): Observable<ChaveMcp> {
    return this.http.post<ChaveMcp>(`${BASE}/${idPublico}/revogar`, {});
  }
}
