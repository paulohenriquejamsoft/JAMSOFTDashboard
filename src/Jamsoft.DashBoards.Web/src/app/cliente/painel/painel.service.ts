import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * Filtro comum aos 4 blocos: datas locais inclusivas `yyyy-MM-dd`, IDs públicos de empresa, tipos de documento e
 * códigos de vendedor (lista vazia = todos).
 */
export interface FiltroPainel {
  inicio: string;
  fim: string;
  empresas: string[];
  tipos: TipoDocumento[];
  vendedores: number[];
}

/** Domínio de `TIPO_DFE` na API. */
export const TIPOS_DOCUMENTO = ['NF-e', 'NFC-e', 'Pedido'] as const;
export type TipoDocumento = (typeof TIPOS_DOCUMENTO)[number];

/** Opção do filtro de vendedor: código e último nome recebido. */
export interface Vendedor {
  codigo: number;
  nome: string;
}

export interface Periodo {
  inicio: string;
  fim: string;
}

/** Valor no período, no anterior e variação % (nula quando o anterior é 0) (DASH-09). */
export interface Indicador {
  atual: number;
  anterior: number;
  variacao: number | null;
}

/** `KpisResposta` da API: os 5 KPIs vêm aninhados em `kpis` (ver SPEC_DEVIATION em RespostasPainel.cs). */
export interface KpisResposta {
  periodo: Periodo;
  anterior: Periodo;
  semVendas: boolean;
  kpis: {
    faturamento: Indicador;
    vendas: Indicador;
    ticketMedio: Indicador;
    itensPorVenda: Indicador;
    percentualCanceladas: Indicador;
  };
}

/** `data`: o dia, ou o dia 1 do mês (`yyyy-MM-dd`); `rotulo`: dd/MM ou MM/yyyy. */
export interface PontoEvolucao {
  rotulo: string;
  data: string;
  atual: number;
  anterior: number;
}

export interface EvolucaoResposta {
  granularidade: 'dia' | 'mes';
  pontos: PontoEvolucao[];
}

/** `participacao`: % do faturamento total do período (RNK-02). */
export interface ItemRanking {
  nome: string;
  faturamento: number;
  vendas: number;
  participacao: number;
}

export interface RankingsResposta {
  totalFaturamento: number;
  vendedores: ItemRanking[];
  clientes: ItemRanking[];
  empresas: ItemRanking[];
}

export interface ItemDfe {
  tipo: TipoDocumento;
  faturamento: number;
  vendas: number;
  participacao: number;
}

export interface DfeResposta {
  tipos: ItemDfe[];
}

const BASE = '/api/painel';

/**
 * Chamadas de `/api/painel/*`, uma por bloco. Passam pelo `authInterceptor` (token, `X-Tenant` do SuperAdmin e
 * renovação em 401, UI-05).
 */
@Injectable({ providedIn: 'root' })
export class PainelService {
  private readonly http = inject(HttpClient);

  kpis(filtro: FiltroPainel): Observable<KpisResposta> {
    return this.http.get<KpisResposta>(`${BASE}/kpis`, { params: parametros(filtro) });
  }

  evolucao(filtro: FiltroPainel): Observable<EvolucaoResposta> {
    return this.http.get<EvolucaoResposta>(`${BASE}/evolucao`, { params: parametros(filtro) });
  }

  rankings(filtro: FiltroPainel): Observable<RankingsResposta> {
    return this.http.get<RankingsResposta>(`${BASE}/rankings`, { params: parametros(filtro) });
  }

  dfe(filtro: FiltroPainel): Observable<DfeResposta> {
    return this.http.get<DfeResposta>(`${BASE}/dfe`, { params: parametros(filtro) });
  }

  /** Vendedores do tenant para o filtro, em ordem alfabética. */
  vendedores(): Observable<Vendedor[]> {
    return this.http.get<Vendedor[]>(`${BASE}/vendedores`);
  }
}

/** `inicio`, `fim` e `empresas`, `tipos` e `vendedores` repetidos uma vez por item; lista vazia não vai. */
function parametros(filtro: FiltroPainel): HttpParams {
  let params = new HttpParams().set('inicio', filtro.inicio).set('fim', filtro.fim);
  for (const empresa of filtro.empresas) params = params.append('empresas', empresa);
  for (const tipo of filtro.tipos) params = params.append('tipos', tipo);
  for (const vendedor of filtro.vendedores) params = params.append('vendedores', vendedor);
  return params;
}
