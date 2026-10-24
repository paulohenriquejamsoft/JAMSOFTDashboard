import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { responderProblema } from '../../../testes/problemas';
import { JobExclusao, SituacaoExclusao } from './exclusao.api';
import { ExclusaoTenantDialog, INTERVALO_POLLING_MS } from './exclusao-tenant.dialog';

const TENANT = { idPublico: 'id-loja-a', nome: 'Loja Ação' };
const JOB = '/api/admin/exclusoes/job-1';

function job(situacao: SituacaoExclusao, extras: Partial<JobExclusao> = {}): JobExclusao {
  return {
    jobId: 'job-1',
    tenantIdPublico: TENANT.idPublico,
    tenantNome: TENANT.nome,
    situacao,
    etapaAtual: null,
    linhasRemovidas: {},
    erro: null,
    criadoEm: '2026-10-24T12:00:00Z',
    iniciadoEm: null,
    heartbeatEm: null,
    concluidoEm: null,
    ...extras,
  };
}

describe('ExclusaoTenantDialog', () => {
  let fixture: ComponentFixture<ExclusaoTenantDialog>;
  let http: HttpTestingController;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
    TestBed.configureTestingModule({
      imports: [ExclusaoTenantDialog],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExclusaoTenantDialog);
    fixture.componentRef.setInput('tenant', TENANT);
    await renderizar();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
    vi.useRealTimers();
  });

  const tela = () => fixture.nativeElement as HTMLElement;
  const botao = (texto: string) =>
    [...tela().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === texto);
  const situacao = () => tela().querySelector('[data-teste=situacao]')?.textContent?.trim();

  async function renderizar(): Promise<void> {
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
  }

  async function digitar(valor: string): Promise<void> {
    const campo = tela().querySelector<HTMLInputElement>('#exclusao-confirmacao')!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
    await renderizar();
  }

  /** Confirma com o nome certo e responde 202; o polling consulta o job na hora. */
  async function solicitar(): Promise<void> {
    await digitar(TENANT.nome);
    botao('Excluir definitivamente')!.click();
    const req = http.expectOne(`/api/admin/tenants/${TENANT.idPublico}/exclusao`);
    expect(req.request.body).toEqual({ confirmacaoNome: TENANT.nome });
    req.flush({ jobId: 'job-1' }, { status: 202, statusText: 'Accepted' });
    await renderizar();
  }

  async function avancarPolling(): Promise<void> {
    await vi.advanceTimersByTimeAsync(INTERVALO_POLLING_MS);
    fixture.detectChanges();
  }

  it('confirmar só habilita com o nome idêntico ao do tenant', async () => {
    const confirmar = () => botao('Excluir definitivamente')!;
    expect(confirmar().disabled).toBe(true);

    for (const quase of ['loja ação', 'Loja Acao', 'Loja Ação ']) {
      await digitar(quase);
      expect(confirmar().disabled).toBe(true);
    }
    await digitar('Loja Ação');
    expect(confirmar().disabled).toBe(false);
  });

  it('202 inicia o acompanhamento a cada 5 s com situação, etapa e linhas', async () => {
    await solicitar();

    http.expectOne(JOB).flush(job('Pendente'));
    await renderizar();
    expect(situacao()).toBe('Pendente');

    await vi.advanceTimersByTimeAsync(INTERVALO_POLLING_MS - 1);
    http.expectNone(JOB);
    await vi.advanceTimersByTimeAsync(1);
    http.expectOne(JOB).flush(job('EmExecucao', { etapaAtual: 'USUARIO', linhasRemovidas: { EMPRESA: 1200 } }));
    fixture.detectChanges();

    expect(situacao()).toBe('Em execução');
    expect(tela().querySelector('[data-teste=etapa]')?.textContent?.trim()).toBe('USUARIO');
    const celulas = [...tela().querySelectorAll('tbody tr td')].map((td) => td.textContent?.trim());
    expect(celulas).toEqual(['EMPRESA', '1200']);
  });

  it('Concluida encerra o polling', async () => {
    await solicitar();
    http.expectOne(JOB).flush(job('EmExecucao'));
    await avancarPolling();
    http.expectOne(JOB).flush(job('Concluida', { etapaAtual: 'TENANT', linhasRemovidas: { TENANT: 1 } }));
    fixture.detectChanges();

    expect(situacao()).toBe('Concluída');
    expect(tela().textContent).toContain('Todos os dados do tenant foram excluídos.');
    await vi.advanceTimersByTimeAsync(INTERVALO_POLLING_MS * 3);
    http.expectNone(JOB);
  });

  it('Falhou mostra o erro e Retomar reinicia o acompanhamento', async () => {
    await solicitar();
    http.expectOne(JOB).flush(job('Falhou', { etapaAtual: 'USUARIO', erro: 'Etapa USUARIO: SqlException 1205: deadlock' }));
    await renderizar();

    expect(situacao()).toBe('Falhou');
    expect(tela().querySelector('[role=alert]')?.textContent).toContain('Etapa USUARIO: SqlException 1205: deadlock');
    await vi.advanceTimersByTimeAsync(INTERVALO_POLLING_MS * 2);
    http.expectNone(JOB);

    botao('Retomar')!.click();
    http.expectOne(`${JOB}/retomar`).flush({ jobId: 'job-1' }, { status: 202, statusText: 'Accepted' });
    await renderizar();
    http.expectOne(JOB).flush(job('Pendente', { etapaAtual: 'USUARIO' }));
    await renderizar();

    expect(situacao()).toBe('Pendente');
    expect(botao('Retomar')).toBeUndefined();
  });

  it('EXCLUSAO_EM_ANDAMENTO mostra o job atual informado pela API', async () => {
    await digitar(TENANT.nome);
    botao('Excluir definitivamente')!.click();
    responderProblema(http.expectOne(`/api/admin/tenants/${TENANT.idPublico}/exclusao`), 409, 'EXCLUSAO_EM_ANDAMENTO', {
      jobId: 'job-1',
    });
    await renderizar();

    http.expectOne(JOB).flush(job('EmExecucao', { etapaAtual: 'EMPRESA' }));
    await renderizar();

    expect(tela().textContent).toContain('Já existe uma exclusão em andamento para este tenant.');
    expect(situacao()).toBe('Em execução');
    expect(tela().querySelector('[data-teste=etapa]')?.textContent?.trim()).toBe('EMPRESA');
  });
});
