import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { responderProblema } from '../../../testes/problemas';
import { AuthService, Perfil, UsuarioSessao } from '../../core/auth/auth.service';
import { RELOGIO } from '../../shared/layout/status-dados.component';
import { DfeResposta, EvolucaoResposta, Indicador, KpisResposta, RankingsResposta } from './painel.service';
import { PainelPage } from './painel.page';

const BLOCOS = ['kpis', 'evolucao', 'rankings', 'dfe'] as const;
type NomeBloco = (typeof BLOCOS)[number];

const zero: Indicador = { atual: 0, anterior: 0, variacao: null };
function kpis(semVendas: boolean): KpisResposta {
  const faturamento: Indicador = semVendas ? zero : { atual: 1000, anterior: 800, variacao: 25 };
  return {
    periodo: { inicio: '2026-10-01', fim: '2026-10-24' },
    anterior: { inicio: '2026-09-07', fim: '2026-09-30' },
    semVendas,
    kpis: { faturamento, vendas: zero, ticketMedio: zero, itensPorVenda: zero, percentualCanceladas: zero },
  };
}
const RESPOSTAS: Record<NomeBloco, object> = {
  kpis: kpis(false),
  evolucao: { granularidade: 'dia', pontos: [] } satisfies EvolucaoResposta,
  rankings: { totalFaturamento: 0, vendedores: [], clientes: [], empresas: [] } satisfies RankingsResposta,
  dfe: { tipos: [] } satisfies DfeResposta,
};

describe('PainelPage', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  /** Abre `/cliente` em 24/10/2026 (São Paulo) e responde empresas e resumo de sincronização. */
  async function abrir(perfil: Perfil = 'Admin', ultimoSucessoEm: string | null = '2026-10-24T10:00:00Z') {
    const sessao: UsuarioSessao = {
      perfil,
      tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
      trocarSenha: false,
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'cliente', component: PainelPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { usuario: signal(sessao).asReadonly() } },
        { provide: RELOGIO, useValue: () => Date.parse('2026-10-24T15:00:00Z') },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/cliente');
    if (perfil === 'SuperAdmin') return;
    http.expectOne('/api/empresas').flush([]);
    http.expectOne('/api/sincronizacao/resumo').flush({ ultimoSucessoEm });
    await harness.fixture.whenStable();
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  const tela = () => harness.routeNativeElement!;
  const bloco = (nome: NomeBloco) => tela().querySelector<HTMLElement>(`[data-bloco=${nome}]`)!;
  const pendente = (nome: NomeBloco): TestRequest => http.expectOne((r) => r.url === `/api/painel/${nome}`);

  async function responderTodos(sobrescrever: Partial<Record<NomeBloco, object>> = {}): Promise<void> {
    for (const nome of BLOCOS) pendente(nome).flush(sobrescrever[nome] ?? RESPOSTAS[nome]);
    await harness.fixture.whenStable();
  }

  it('ao abrir, dispara os 4 blocos com o mês corrente', async () => {
    await abrir();

    for (const nome of BLOCOS) {
      const req = pendente(nome);
      expect(req.request.urlWithParams, nome).toBe(`/api/painel/${nome}?inicio=2026-10-01&fim=2026-10-24`);
      req.flush(RESPOSTAS[nome]);
    }
  });

  it('cada bloco mostra carregando enquanto a consulta está pendente', async () => {
    await abrir();

    for (const nome of BLOCOS) {
      expect(bloco(nome).querySelector('[role=status]')?.textContent?.trim(), nome).toBe('Carregando...');
      expect(bloco(nome).querySelector('.bloco-conteudo'), nome).toBeNull();
    }

    pendente('kpis').flush(RESPOSTAS.kpis);
    await harness.fixture.whenStable();
    expect(bloco('kpis').querySelector('[role=status]')).toBeNull();
    expect(bloco('kpis').querySelector('.bloco-conteudo')).not.toBeNull();
    expect(bloco('dfe').querySelector('[role=status]')?.textContent?.trim()).toBe('Carregando...');
    for (const nome of ['evolucao', 'rankings', 'dfe'] as const) pendente(nome).flush(RESPOSTAS[nome]);
  });

  it('erro em rankings mostra erro e "Tentar novamente" só nele; os outros 3 renderizam', async () => {
    await abrir();

    pendente('kpis').flush(RESPOSTAS.kpis);
    pendente('evolucao').flush(RESPOSTAS.evolucao);
    responderProblema(pendente('rankings'), 500, 'ERRO_INTERNO');
    pendente('dfe').flush(RESPOSTAS.dfe);
    await harness.fixture.whenStable();

    expect(bloco('rankings').querySelector('[role=alert]')?.textContent).toContain('Erro inesperado');
    expect(bloco('rankings').querySelector('button')?.textContent?.trim()).toBe('Tentar novamente');
    expect(bloco('rankings').querySelector('.bloco-conteudo')).toBeNull();
    for (const nome of ['kpis', 'evolucao', 'dfe'] as const) {
      expect(bloco(nome).querySelector('.bloco-conteudo'), nome).not.toBeNull();
      expect(bloco(nome).querySelector('[role=alert]'), nome).toBeNull();
    }
  });

  it('"Tentar novamente" refaz só o bloco que falhou', async () => {
    await abrir();
    pendente('kpis').flush(RESPOSTAS.kpis);
    pendente('evolucao').flush(RESPOSTAS.evolucao);
    responderProblema(pendente('rankings'), 500, 'ERRO_INTERNO');
    pendente('dfe').flush(RESPOSTAS.dfe);
    await harness.fixture.whenStable();

    bloco('rankings').querySelector('button')!.click();
    await harness.fixture.whenStable();

    const repetida = pendente('rankings');
    expect(repetida.request.urlWithParams).toBe('/api/painel/rankings?inicio=2026-10-01&fim=2026-10-24');
    for (const nome of ['kpis', 'evolucao', 'dfe'] as const) http.expectNone((r) => r.url === `/api/painel/${nome}`);
    expect(bloco('rankings').querySelector('[role=status]')?.textContent?.trim()).toBe('Carregando...');
    repetida.flush(RESPOSTAS.rankings);
    await harness.fixture.whenStable();
    expect(bloco('rankings').querySelector('.bloco-conteudo')).not.toBeNull();
  });

  it('semVendas mostra "Nenhuma venda no período"', async () => {
    await abrir();
    expect(tela().textContent).not.toContain('Nenhuma venda no período');

    await responderTodos({ kpis: kpis(true) });

    expect(tela().textContent).toContain('Nenhuma venda no período');
    expect(bloco('kpis').querySelector('.bloco-conteudo')).not.toBeNull();
  });

  it('tenant que nunca sincronizou mostra "Nenhuma venda sincronizada ainda" no lugar dos blocos', async () => {
    await abrir('Admin', null);
    await responderTodos({ kpis: kpis(true) });

    expect(tela().textContent).toContain('Nenhuma venda sincronizada ainda');
    expect(tela().textContent).not.toContain('Nenhuma venda no período');
    expect(tela().querySelector('[data-bloco]')).toBeNull();
  });

  it('SuperAdmin sem tenant vê o seletor e nada é consultado', async () => {
    await abrir('SuperAdmin');
    await harness.fixture.whenStable();

    expect(tela().querySelector('app-seletor-tenant')).not.toBeNull();
    expect(tela().querySelector('[role=status]')?.textContent?.trim()).toBe('Selecione um tenant');
    expect(tela().querySelector('app-filtro-painel')).toBeNull();
    http.expectNone((r) => r.url.startsWith('/api/painel/'));
    http.expectNone('/api/empresas');
    http.expectNone('/api/sincronizacao/resumo');
  });
});
