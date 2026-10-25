// Lighthouse e instalabilidade do painel (T22 de painel-vendas).
//
// Serve o build de produção do Angular (com service worker) em http://localhost:4300, com /api repassado para a API
// (API_URL), e mede:
// - Acessibilidade: Lighthouse 13 no Chrome instalado, na tela de login e no painel (/cliente). Para o painel, o
//   cookie de refresh de um login do Admin vai em --extra-headers; a app restaura a sessão com ele ao abrir.
// - PWA instalável: o Lighthouse 12+ não tem mais a categoria PWA e o 11 não conversa com o Chrome atual
//   (PROTOCOL_TIMEOUT). A instalabilidade vem do próprio Chromium (CDP `Page.getInstallabilityErrors`, o mesmo
//   critério que o Lighthouse 11 usava), com o service worker já ativo.
//
// Pré-requisitos: `npm --prefix src/Jamsoft.DashBoards.Web run build`, Chrome, API no ar e um Admin com senha já
// trocada.
// Uso: API_URL=http://localhost:5180 ADMIN_EMAIL=... ADMIN_SENHA=... SAIDA=<pasta> node lighthouse-painel.mjs
import { execFile } from 'node:child_process';
import { createReadStream, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import http from 'node:http';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';

const raiz = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const web = path.join(raiz, 'src', 'Jamsoft.DashBoards.Web');
const dist = path.join(web, 'dist', 'jamsoft-dashboards-web', 'browser');
const { chromium } = createRequire(path.join(web, 'package.json'))('@playwright/test');

const API = process.env.API_URL ?? 'http://localhost:5180';
const EMAIL = process.env.ADMIN_EMAIL;
const SENHA = process.env.ADMIN_SENHA;
const SAIDA = process.env.SAIDA ?? mkdtempSync(path.join(os.tmpdir(), 'lighthouse-painel-'));
const PORTA = 4300;
const BASE = `http://localhost:${PORTA}`;
if (!EMAIL || !SENHA) throw new Error('Informe ADMIN_EMAIL e ADMIN_SENHA.');
if (!existsSync(path.join(dist, 'ngsw-worker.js'))) throw new Error(`Build de produção não encontrado em ${dist}.`);
mkdirSync(SAIDA, { recursive: true });

const TIPOS = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json',
  '.webmanifest': 'application/manifest+json',
  '.png': 'image/png',
  '.webp': 'image/webp',
  '.ico': 'image/x-icon',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
};

// Arquivos estáticos com fallback para index.html (rotas do Angular) e /api repassado para a API.
const servidor = http.createServer((req, res) => {
  const url = new URL(req.url, BASE);
  if (url.pathname.startsWith('/api/')) {
    const alvo = new URL(url.pathname + url.search, API);
    const repasse = http.request(alvo, { method: req.method, headers: { ...req.headers, host: alvo.host } }, (r) => {
      res.writeHead(r.statusCode ?? 502, r.headers);
      r.pipe(res);
    });
    repasse.on('error', () => res.writeHead(502).end());
    req.pipe(repasse);
    return;
  }
  let arquivo = path.join(dist, decodeURIComponent(url.pathname));
  if (!arquivo.startsWith(dist) || !existsSync(arquivo) || statSync(arquivo).isDirectory()) {
    arquivo = path.join(dist, 'index.html');
  }
  res.writeHead(200, { 'Content-Type': TIPOS[path.extname(arquivo)] ?? 'application/octet-stream' });
  createReadStream(arquivo).pipe(res);
});
await new Promise((ok) => servidor.listen(PORTA, ok));

/** Cookie de refresh de um login novo do Admin (o refresh token é rotativo: um por execução do Lighthouse). */
async function cookieDoAdmin() {
  const resposta = await fetch(`${API}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, senha: SENHA }),
  });
  if (!resposta.ok) throw new Error(`Login do Admin falhou: ${resposta.status}`);
  return resposta.headers.getSetCookie().map((c) => c.split(';')[0]).join('; ');
}

/**
 * Roda o Lighthouse 13 (só Acessibilidade) no Chrome instalado (perfil limpo, headless). O Chrome headless às vezes
 * não pinta a tempo (NO_FCP) nesta máquina: até 3 tentativas, cada uma com cabeçalhos novos.
 */
async function lighthouse(nome, rota, cabecalhos) {
  const arquivo = path.join(SAIDA, `lighthouse-${nome}.json`);
  // Arquivo em vez de JSON na linha de comando: evita o escape de aspas do cmd.exe.
  const arquivoCabecalhos = path.join(SAIDA, `cabecalhos-${nome}.json`);
  for (let tentativa = 1; ; tentativa++) {
    if (cabecalhos) writeFileSync(arquivoCabecalhos, JSON.stringify(await cabecalhos()));
    try {
      // Assíncrono: o servidor estático acima roda neste mesmo processo e precisa do event loop livre.
      await promisify(execFile)(
        'npx',
        [
          '-y',
          'lighthouse@13.5.0',
          `${BASE}${rota}`,
          '--chrome-flags=--headless=new',
          ...(cabecalhos ? [`--extra-headers=${arquivoCabecalhos}`] : []),
          '--only-categories=accessibility',
          '--output=json',
          `--output-path=${arquivo}`,
          '--quiet',
        ],
        { shell: process.platform === 'win32', maxBuffer: 64 * 1024 * 1024 },
      );
      break;
    } catch (erro) {
      if (tentativa === 3) throw erro;
    } finally {
      rmSync(arquivoCabecalhos, { force: true });
    }
  }
  const relatorio = JSON.parse(readFileSync(arquivo, 'utf8'));
  const pontos = (c) => (relatorio.categories[c]?.score == null ? null : Math.round(relatorio.categories[c].score * 100));
  return {
    pagina: nome,
    url: relatorio.finalDisplayedUrl,
    lighthouse: relatorio.lighthouseVersion,
    acessibilidade: pontos('accessibility'),
    falhasAcessibilidade: relatorio.categories.accessibility.auditRefs
      .filter((r) => r.weight > 0 && relatorio.audits[r.id].score !== null && relatorio.audits[r.id].score < 1)
      .map((r) => r.id),
    arquivo,
  };
}

/** Critério de instalação do Chromium com o service worker ativo: lista vazia = instalável (PWA-01). */
async function instalabilidade() {
  const navegador = await chromium.launch();
  try {
    const pagina = await navegador.newPage();
    await pagina.goto(`${BASE}/conta/login`);
    // `registerWhenStable:30000`: o service worker é registrado em até 30 s.
    await pagina.evaluate(() => navigator.serviceWorker.ready.then(() => true));
    await pagina.reload();
    const cdp = await pagina.context().newCDPSession(pagina);
    const { installabilityErrors } = await cdp.send('Page.getInstallabilityErrors');
    const { errors: errosManifesto } = await cdp.send('Page.getAppManifest');
    const controlada = await pagina.evaluate(() => navigator.serviceWorker.controller !== null);
    return {
      navegador: navegador.version(),
      instalavel: installabilityErrors.length === 0 && errosManifesto.length === 0,
      errosInstalacao: installabilityErrors.map((e) => e.errorId),
      errosManifesto: errosManifesto.map((e) => e.message),
      serviceWorkerControlaAPagina: controlada,
    };
  } finally {
    await navegador.close();
  }
}

const resultados = {};
try {
  resultados.instalacao = await instalabilidade();
  resultados.acessibilidade = [
    await lighthouse('login', '/conta/login'),
    await lighthouse('painel', '/cliente', async () => ({ Cookie: await cookieDoAdmin() })),
  ];
} finally {
  servidor.close();
}
console.log(JSON.stringify(resultados, null, 2));
