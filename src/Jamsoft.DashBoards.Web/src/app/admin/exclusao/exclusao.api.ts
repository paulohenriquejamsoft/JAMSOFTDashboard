import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type SituacaoExclusao = 'Pendente' | 'EmExecucao' | 'Concluida' | 'Falhou';

/** Job de exclusão como sai da API (`ExclusaoResposta`); datas em UTC. */
export interface JobExclusao {
  jobId: string;
  tenantIdPublico: string;
  tenantNome: string;
  situacao: SituacaoExclusao;
  etapaAtual: string | null;
  linhasRemovidas: Record<string, number>;
  erro: string | null;
  criadoEm: string;
  iniciadoEm: string | null;
  heartbeatEm: string | null;
  concluidoEm: string | null;
}

export interface JobCriado {
  jobId: string;
}

/** Rotas da exclusão de tenant (DEL-02, DEL-03, DEL-06), somente SuperAdmin. */
@Injectable({ providedIn: 'root' })
export class ExclusaoApi {
  private readonly http = inject(HttpClient);

  solicitar(tenantIdPublico: string, confirmacaoNome: string): Observable<JobCriado> {
    return this.http.post<JobCriado>(`/api/admin/tenants/${tenantIdPublico}/exclusao`, { confirmacaoNome });
  }

  obter(jobId: string): Observable<JobExclusao> {
    return this.http.get<JobExclusao>(`/api/admin/exclusoes/${jobId}`);
  }

  retomar(jobId: string): Observable<JobCriado> {
    return this.http.post<JobCriado>(`/api/admin/exclusoes/${jobId}/retomar`, null);
  }
}
