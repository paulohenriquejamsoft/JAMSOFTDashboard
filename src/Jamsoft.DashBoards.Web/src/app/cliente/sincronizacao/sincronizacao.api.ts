import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type SituacaoExecucao = 'Sucesso' | 'Falha' | 'Cancelada';

/** Origem do tenant com a última execução (`OrigemSincronizacaoResposta`); datas em UTC ISO. */
export interface OrigemSincronizacao {
  idPublico: string;
  configuracaoId: string;
  rotina: string;
  bancoOrigem: string | null;
  ultimoSucessoEm: string | null;
  ultimaSituacao: SituacaoExecucao | null;
  ultimaMensagem: string | null;
  ultimaExecucaoInicio: string | null;
  ultimaExecucaoFim: string | null;
  registrosUltima: number | null;
}

/** Última sincronização bem-sucedida do tenant (`ResumoSincronizacaoResposta`); `null` se ainda não houve. */
export interface ResumoSincronizacao {
  ultimoSucessoEm: string | null;
}

const BASE = '/api/sincronizacao';

/** Chamadas de `/api/sincronizacao` (STS-01, STS-02) no tenant efetivo. */
@Injectable({ providedIn: 'root' })
export class SincronizacaoApi {
  private readonly http = inject(HttpClient);

  /** Origens do tenant (Admin ou SuperAdmin com tenant). */
  listar(): Observable<OrigemSincronizacao[]> {
    return this.http.get<OrigemSincronizacao[]>(BASE);
  }

  /** Resumo para o cabeçalho (qualquer perfil do tenant). */
  resumo(): Observable<ResumoSincronizacao> {
    return this.http.get<ResumoSincronizacao>(`${BASE}/resumo`);
  }
}
