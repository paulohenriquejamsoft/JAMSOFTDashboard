import { estiloSerieAnterior, estiloSerieAtual, estiloTexto, lerTemaGraficos } from './tema-graficos';

/** Valores diferentes dos do DS: provam que o tema lê os tokens, não constantes. */
const TOKENS: Record<string, string> = {
  '--primary': '#123456',
  '--text-muted': '#abcdef',
  '--text-secondary': '#202020',
  '--font': "'Inter', system-ui, sans-serif",
  '--font-mono': "'JetBrains Mono', monospace",
};

describe('tema dos gráficos', () => {
  const raiz = document.documentElement;

  beforeEach(() => Object.entries(TOKENS).forEach(([nome, valor]) => raiz.style.setProperty(nome, valor)));
  afterEach(() => Object.keys(TOKENS).forEach((nome) => raiz.style.removeProperty(nome)));

  it('série atual usa --primary contínua e a anterior usa --text-muted tracejada', () => {
    const tema = lerTemaGraficos();

    expect(estiloSerieAtual(tema)).toEqual({
      itemStyle: { color: '#123456' },
      lineStyle: { color: '#123456', width: 2, type: 'solid' },
    });
    expect(estiloSerieAnterior(tema)).toEqual({
      itemStyle: { color: '#abcdef' },
      lineStyle: { color: '#abcdef', width: 2, type: 'dashed' },
    });
  });

  it('fontes vêm dos tokens: Inter no texto e JetBrains Mono nos valores', () => {
    const tema = lerTemaGraficos();

    expect(estiloTexto(tema)).toEqual({ fontFamily: "'Inter', system-ui, sans-serif", color: '#202020' });
    expect(tema.fonteMono).toBe("'JetBrains Mono', monospace");
  });
});
