import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { AuthService, UsuarioSessao } from '../../core/auth/auth.service';
import { provideFormatacaoPtBr } from '../../core/formatacao/formatacao';
import { ChaveMcp } from './chaves-mcp.api';
import { ChavesMcpPage, formatarDia, hojeEmSaoPaulo } from './chaves-mcp.page';
import { environment } from '../../../environments/environment';

function chave(prefixo: string, descricao: string, extras: Partial<ChaveMcp> = {}): ChaveMcp {
  return {
    idPublico: `id-${prefixo}`,
    prefixo,
    descricao,
    ativo: true,
    criadoEm: '2026-10-20T13:00:00Z',
    expiraEm: null,
    revogadoEm: null,
    ...extras,
  };
}

const CHAVE = 'sge_ABCDEFGH_abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRS';

describe('ChavesMcpPage', () => {
  let fixture: ComponentFixture<ChavesMcpPage>;
  let http: HttpTestingController;

  async function criar(): Promise<void> {
    const sessao: UsuarioSessao = { perfil: 'Admin', tenant: { idPublico: 'a1', nome: 'Loja A' }, trocarSenha: false };
    TestBed.configureTestingModule({
      imports: [ChavesMcpPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFormatacaoPtBr(),
        { provide: AuthService, useValue: { usuario: signal(sessao).asReadonly() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ChavesMcpPage);
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  const tela = () => fixture.nativeElement as HTMLElement;
  const linhas = () =>
    [...tela().querySelectorAll('tbody tr')].map((tr) =>
      [...tr.querySelectorAll('td')].map((td) => td.textContent!.replace(/\s+/g, ' ').trim()).join(' | '),
    );
  const botao = (texto: string) =>
    [...tela().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === texto)!;

  function digitar(id: string, valor: string): void {
    const campo = tela().querySelector<HTMLInputElement>(`#${id}`)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  }

  async function carregar(chaves: ChaveMcp[]): Promise<void> {
    http.expectOne({ method: 'GET', url: '/api/chaves-mcp' }).flush(chaves);
    await fixture.whenStable();
  }

  it('lista prefixo, descrição, situação e validade, sem botão de revogar nas revogadas', async () => {
    await criar();
    await carregar([
      chave('AAAAAAAA', 'Claude - Painéis', { expiraEm: '2099-12-31' }),
      chave('BBBBBBBB', 'Antiga', { expiraEm: '2020-01-31' }),
      chave('CCCCCCCC', 'Revogada', { ativo: false, revogadoEm: '2026-10-21T10:00:00Z' }),
    ]);

    expect(linhas()).toEqual([
      'sge_AAAAAAAA_… | Claude - Painéis | Ativa | 20/10/2026 10:00 | 31/12/2099 | Revogar',
      'sge_BBBBBBBB_… | Antiga | Vencida | 20/10/2026 10:00 | 31/01/2020 | Revogar',
      'sge_CCCCCCCC_… | Revogada | Revogada | 20/10/2026 10:00 | Sem validade | ',
    ]);
  });

  it('gerar envia descrição e validade, mostra a chave uma vez e acrescenta a linha no topo', async () => {
    await criar();
    await carregar([chave('AAAAAAAA', 'Outra')]);

    digitar('chave-descricao', '  Claude - Painéis ');
    digitar('chave-expira', '2099-12-31');
    botao('Gerar chave').click();
    const req = http.expectOne({ method: 'POST', url: '/api/chaves-mcp' });
    expect(req.request.body).toEqual({ descricao: 'Claude - Painéis', expiraEm: '2099-12-31' });
    req.flush({ chave: CHAVE, registro: chave('ABCDEFGH', 'Claude - Painéis', { expiraEm: '2099-12-31' }) },
      { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    expect(tela().querySelector<HTMLInputElement>('#chave-mcp-valor')!.value).toBe(CHAVE);
    expect(tela().querySelector<HTMLInputElement>('#chave-mcp-url')!.value).toBe(environment.mcpUrl);
    expect(linhas()[0]).toContain('sge_ABCDEFGH_…');
    expect(tela().querySelector<HTMLInputElement>('#chave-descricao')!.value).toBe('');

    botao('Fechar').click();
    await fixture.whenStable();
    expect(tela().querySelector('#chave-mcp-valor')).toBeNull();
    expect(tela().textContent).not.toContain(CHAVE);
  });

  it('sem validade envia expiraEm nulo; descrição vazia não envia', async () => {
    await criar();
    await carregar([]);

    botao('Gerar chave').click();
    await fixture.whenStable();
    expect(tela().querySelector('#chave-descricao-erro')?.textContent?.trim()).toBe(
      'A descrição precisa ter entre 1 e 80 caracteres.',
    );
    http.expectNone({ method: 'POST', url: '/api/chaves-mcp' });

    digitar('chave-descricao', 'Claude');
    botao('Gerar chave').click();
    const req = http.expectOne({ method: 'POST', url: '/api/chaves-mcp' });
    expect(req.request.body).toEqual({ descricao: 'Claude', expiraEm: null });
    req.flush({ chave: CHAVE, registro: chave('ABCDEFGH', 'Claude') }, { status: 201, statusText: 'Created' });
    await fixture.whenStable();
  });

  it('VALIDACAO da API aparece no campo da validade', async () => {
    await criar();
    await carregar([]);

    digitar('chave-descricao', 'Claude');
    digitar('chave-expira', '2020-01-01');
    botao('Gerar chave').click();
    responderProblema(http.expectOne({ method: 'POST', url: '/api/chaves-mcp' }), 400, 'VALIDACAO', {
      erros: { expiraEm: ['A validade não pode ser anterior a hoje.'] },
    });
    await fixture.whenStable();

    expect(tela().querySelector('#chave-expira-erro')?.textContent?.trim()).toBe('A validade não pode ser anterior a hoje.');
  });

  it('revogar pede confirmação e atualiza a linha', async () => {
    await criar();
    await carregar([chave('AAAAAAAA', 'Claude'), chave('BBBBBBBB', 'Outra')]);

    tela().querySelectorAll('tbody tr')[0].querySelector('button')!.click();
    await fixture.whenStable();
    expect(tela().querySelector('[role=alertdialog]')?.textContent).toContain('Claude');
    tela().querySelector<HTMLButtonElement>('[role=alertdialog] .btn-primario')!.click();
    http
      .expectOne({ method: 'POST', url: '/api/chaves-mcp/id-AAAAAAAA/revogar' })
      .flush(chave('AAAAAAAA', 'Claude', { ativo: false, revogadoEm: '2026-10-26T12:00:00Z' }));
    await fixture.whenStable();

    expect(tela().querySelector('[role=alertdialog]')).toBeNull();
    expect(linhas()).toEqual([
      'sge_BBBBBBBB_… | Outra | Ativa | 20/10/2026 10:00 | Sem validade | Revogar',
      'sge_AAAAAAAA_… | Claude | Revogada | 20/10/2026 10:00 | Sem validade | ',
    ]);
  });

  it('datas sem fuso: hoje em São Paulo e dia formatado sem deslocar', () => {
    expect(hojeEmSaoPaulo(new Date('2026-10-27T02:30:00Z'))).toBe('2026-10-26');
    expect(formatarDia('2027-01-01')).toBe('01/01/2027');
  });
});
