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

export interface AdminInicial {
  nome: string;
  email: string;
  senhaInicial: string;
}

/** Corpo de `POST /api/admin/tenants`; sem `adminInicial` quando o Admin não é cadastrado junto. */
export interface CriarTenantRequisicao {
  nome: string;
  cnpj: string;
  adminInicial?: AdminInicial;
}

export interface EditarTenantRequisicao {
  nome: string;
  cnpj: string;
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

  obter(idPublico: string): Observable<Tenant> {
    return this.http.get<Tenant>(`${BASE}/${idPublico}`);
  }

  criar(corpo: CriarTenantRequisicao): Observable<Tenant> {
    return this.http.post<Tenant>(BASE, corpo);
  }

  editar(idPublico: string, corpo: EditarTenantRequisicao): Observable<Tenant> {
    return this.http.put<Tenant>(`${BASE}/${idPublico}`, corpo);
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

/** Máscara progressiva 00.000.000/0000-00 sobre os dígitos digitados (até 14). */
export function mascararCnpj(valor: string): string {
  const d = valor.replace(/\D/g, '').slice(0, 14);
  const partes = [d.slice(0, 2), d.slice(2, 5), d.slice(5, 8), d.slice(8, 12), d.slice(12, 14)];
  let texto = partes[0];
  if (partes[1]) texto += '.' + partes[1];
  if (partes[2]) texto += '.' + partes[2];
  if (partes[3]) texto += '/' + partes[3];
  if (partes[4]) texto += '-' + partes[4];
  return texto;
}

/** Mesma regra da API (`Cnpj.TentarNormalizar`): 14 dígitos, não repetidos, verificadores certos. */
export function cnpjValido(valor: string): boolean {
  if (/[^\d./\- ]/.test(valor)) return false;
  const d = valor.replace(/\D/g, '').split('').map(Number);
  if (d.length !== 14 || d.every((n) => n === d[0])) return false;
  const verificador = (pesos: number[]) => {
    const resto = pesos.reduce((soma, peso, i) => soma + peso * d[i], 0) % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  return (
    d[12] === verificador([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) &&
    d[13] === verificador([6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
  );
}
