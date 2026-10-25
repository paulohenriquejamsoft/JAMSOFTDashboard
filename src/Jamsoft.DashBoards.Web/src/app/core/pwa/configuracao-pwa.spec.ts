import manifestoTexto from '../../../../public/manifest.webmanifest' with { loader: 'text' };
import icone192 from '../../../../public/icons/icon-192x192.png' with { loader: 'binary' };
import icone512 from '../../../../public/icons/icon-512x512.png' with { loader: 'binary' };
import ngswConfig from '../../../../ngsw-config.json';

interface Icone {
  src: string;
  sizes: string;
  type: string;
}

const manifesto = JSON.parse(manifestoTexto) as Record<string, unknown> & { icons: Icone[] };

/** Arquivos em disco, pelo caminho que o manifest usa. */
const ARQUIVOS: Record<string, Uint8Array> = {
  'icons/icon-192x192.png': icone192,
  'icons/icon-512x512.png': icone512,
};

/** Largura × altura de um PNG, lidas do cabeçalho IHDR. */
function dimensoesPng(bytes: Uint8Array): string {
  const assinatura = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (!assinatura.every((b, i) => bytes[i] === b)) return 'não é PNG';
  const dados = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  return `${dados.getUint32(16)}x${dados.getUint32(20)}`;
}

/** Todas as strings de um JSON, em qualquer profundidade. */
function strings(valor: unknown): string[] {
  if (typeof valor === 'string') return [valor];
  if (Array.isArray(valor)) return valor.flatMap(strings);
  if (valor && typeof valor === 'object') return Object.values(valor).flatMap(strings);
  return [];
}

describe('Manifest da PWA (PWA-01)', () => {
  it('tem nome "JAMSOFT Dashboards", short_name, display standalone e as cores do Design System', () => {
    expect(manifesto['name']).toBe('JAMSOFT Dashboards');
    expect(manifesto['short_name']).toBe('JAMSOFT');
    expect(manifesto['display']).toBe('standalone');
    expect(String(manifesto['theme_color']).toUpperCase()).toBe('#F2323F');
    expect(String(manifesto['background_color']).toUpperCase()).toBe('#FBFAF7');
  });

  it.each(['192x192', '512x512'])('declara o ícone %s e o arquivo existe no disco com esse tamanho', (tamanho) => {
    const icone = manifesto.icons.find((i) => i.sizes === tamanho);

    expect(icone?.type).toBe('image/png');
    const arquivo = ARQUIVOS[icone!.src];
    expect(arquivo, icone!.src).toBeDefined();
    expect(dimensoesPng(arquivo)).toBe(tamanho);
  });
});

describe('Política de cache do service worker (PWA-02)', () => {
  it('não tem dataGroups: nenhuma resposta da API vai para o cache', () => {
    expect(ngswConfig).not.toHaveProperty('dataGroups');
  });

  it('nenhuma URL do ngsw-config.json começa com /api', () => {
    const urls = strings(ngswConfig);

    expect(urls.length).toBeGreaterThan(0);
    expect(urls.filter((u) => u.replace(/^!/, '').startsWith('/api'))).toEqual([]);
  });
});
