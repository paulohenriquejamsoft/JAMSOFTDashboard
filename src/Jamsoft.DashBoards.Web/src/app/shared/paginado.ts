/** Página de resultados da API (`ResultadoPaginado<T>`): `pagina` começa em 1. */
export interface Paginado<T> {
  itens: T[];
  pagina: number;
  tamanho: number;
  total: number;
}

/** Situação de uma tela que carrega dados da API. */
export type EstadoCarga = 'carregando' | 'pronto' | 'vazio' | 'erro';
