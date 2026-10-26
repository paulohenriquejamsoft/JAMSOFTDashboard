/**
 * Cores e fontes dos gráficos lidas dos tokens do Design System (`styles/_tokens.scss`) no momento em que o
 * gráfico é montado, para seguir também o tema escuro (UI-06, AD-017).
 */
export interface TemaGraficos {
  /** `--primary`: série do período atual e 1ª categoria. */
  primaria: string;
  /** `--text-muted`: série do período anterior, eixos e rótulos secundários. */
  textoSuave: string;
  /** `--text-secondary`: textos dos eixos e da legenda. */
  textoSecundario: string;
  /** `--secundario`: 2ª categoria (rosca por tipo de documento). */
  secundaria: string;
  /** `--atencao`: 3ª categoria (rosca por tipo de documento). */
  terciaria: string;
  /** `--borda-suave`: linhas de grade. */
  grade: string;
  /** `--surface-bg`: fundo do card, usado no espaço entre fatias. */
  superficie: string;
  /** `--font` (Inter). */
  fonte: string;
  /** `--font-mono` (JetBrains Mono), para valores numéricos. */
  fonteMono: string;
}

/** Lê os tokens do DS na raiz do documento. */
export function lerTemaGraficos(raiz: Element = document.documentElement): TemaGraficos {
  const estilo = getComputedStyle(raiz);
  const token = (nome: string) => estilo.getPropertyValue(nome).trim();
  return {
    primaria: token('--primary'),
    textoSuave: token('--text-muted'),
    textoSecundario: token('--text-secondary'),
    secundaria: token('--secundario'),
    terciaria: token('--atencao'),
    grade: token('--borda-suave'),
    superficie: token('--surface-bg'),
    fonte: token('--font'),
    fonteMono: token('--font-mono'),
  };
}

/** Linha do período atual: `--primary`, contínua, 2 px. */
export function estiloSerieAtual(tema: TemaGraficos) {
  return {
    itemStyle: { color: tema.primaria },
    lineStyle: { color: tema.primaria, width: 2, type: 'solid' as const },
  };
}

/** Linha do período anterior: `--text-muted`, tracejada, 2 px. */
export function estiloSerieAnterior(tema: TemaGraficos) {
  return {
    itemStyle: { color: tema.textoSuave },
    lineStyle: { color: tema.textoSuave, width: 2, type: 'dashed' as const },
  };
}

/** Texto padrão dos gráficos (Inter, cor de texto secundário). */
export function estiloTexto(tema: TemaGraficos) {
  return { fontFamily: tema.fonte, color: tema.textoSecundario };
}
