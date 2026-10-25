import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { AuthService, Perfil, UsuarioSessao } from '../../core/auth/auth.service';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';
import { OrigemSincronizacao } from './sincronizacao.api';
import { SincronizacaoPage } from './sincronizacao.page';

/** Datas locais em ISO UTC: o texto esperado não depende do fuso da máquina. */
const SUCESSO = new Date(2026, 9, 24, 15, 30).toISOString();
const FALHA_INICIO = new Date(2026, 9, 25, 8, 0).toISOString();

function origem(rotina: string, extras: Partial<OrigemSincronizacao> = {}): OrigemSincronizacao {
  return {
    idPublico: `id-${rotina}`,
    configuracaoId: `cfg-${rotina}`,
    rotina,
    bancoOrigem: 'ERP_MATRIZ',
    ultimoSucessoEm: SUCESSO,
    ultimaSituacao: 'Sucesso',
    ultimaMensagem: null,
    ultimaExecucaoInicio: SUCESSO,
    ultimaExecucaoFim: SUCESSO,
    registrosUltima: 120,
    ...extras,
  };
}

describe('SincronizacaoPage', () => {
  let fixture: ComponentFixture<SincronizacaoPage>;
  let http: HttpTestingController;

  async function criar(perfil: Perfil = 'Admin'): Promise<void> {
    const sessao: UsuarioSessao = {
      perfil,
      tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
      trocarSenha: false,
    };
    TestBed.configureTestingModule({
      imports: [SincronizacaoPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { usuario: signal(sessao).asReadonly() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SincronizacaoPage);
    await fixture.whenStable();
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  const tela = () => fixture.nativeElement as HTMLElement;
  const linhas = () =>
    [...tela().querySelectorAll('tbody tr')].map((tr) =>
      [...tr.querySelectorAll('td')].map((td) => td.textContent!.replace(/\s+/g, ' ').trim()).join(' | '),
    );

  async function carregar(origens: OrigemSincronizacao[]): Promise<void> {
    http.expectOne({ method: 'GET', url: '/api/sincronizacao' }).flush(origens);
    await fixture.whenStable();
  }

  it('renderiza rotina, banco, último sucesso, situação e mensagem de cada origem', async () => {
    await criar();
    expect(tela().querySelector('[role=status]')?.textContent?.trim()).toBe('Carregando...');

    await carregar([origem('vendas'), origem('vendas-filial', { bancoOrigem: null, ultimoSucessoEm: null, ultimaSituacao: null })]);

    expect(linhas()).toEqual([
      'vendas | ERP_MATRIZ | 24/10/2026 15:30 | Sucesso | —',
      'vendas-filial | — | Nunca | Sem execução | —',
    ]);
  });

  it('falha mostra a situação e a mensagem e mantém a data do último sucesso', async () => {
    await criar();
    await carregar([
      origem('vendas', {
        ultimaSituacao: 'Falha',
        ultimaMensagem: 'Conexão: servidor do ERP inacessível',
        ultimaExecucaoInicio: FALHA_INICIO,
        ultimaExecucaoFim: FALHA_INICIO,
      }),
    ]);

    expect(linhas()).toEqual(['vendas | ERP_MATRIZ | 24/10/2026 15:30 | Falha | Conexão: servidor do ERP inacessível']);
    expect(tela().querySelector('tbody .badge')?.classList).toContain('badge-perigo');
  });

  it('sem origens mostra o estado vazio', async () => {
    await criar();
    await carregar([]);

    expect(tela().textContent).toContain('Nenhuma sincronização recebida para este tenant.');
    expect(tela().querySelector('table')).toBeNull();
  });

  it('erro mostra a mensagem do código e Tentar novamente refaz a consulta', async () => {
    await criar();
    responderProblema(http.expectOne('/api/sincronizacao'), 403, 'ACESSO_NEGADO');
    await fixture.whenStable();

    expect(tela().querySelector('[role=alert]')?.textContent).toContain('Acesso não permitido');

    [...tela().querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Tentar novamente')!.click();
    await carregar([origem('vendas')]);
    expect(linhas()).toEqual(['vendas | ERP_MATRIZ | 24/10/2026 15:30 | Sucesso | —']);
  });

  it('SuperAdmin sem tenant vê o aviso e o seletor, sem consultar; ao escolher um tenant a tabela carrega', async () => {
    await criar('SuperAdmin');

    expect(tela().querySelector('[role=status]')?.textContent?.trim()).toBe('Selecione um tenant');
    expect(tela().querySelector('app-seletor-tenant')?.textContent).toContain('Selecionar tenant');
    http.expectNone('/api/sincronizacao');

    TestBed.inject(TenantContextoService).selecionar({ idPublico: 'b2', nome: 'Loja B' });
    await fixture.whenStable();
    await carregar([origem('vendas')]);

    expect(tela().querySelector('app-seletor-tenant')).toBeNull();
    expect(linhas()).toEqual(['vendas | ERP_MATRIZ | 24/10/2026 15:30 | Sucesso | —']);
  });
});
