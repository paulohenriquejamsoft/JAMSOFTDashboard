import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { Paginado } from '../../shared/paginado';
import { Coletor } from './coletores.api';
import { ColetoresListaPage } from './coletores-lista.page';

/** Data local 24/10/2026 15:30 em ISO UTC: o texto esperado não depende do fuso da máquina. */
const ULTIMO_TOKEN = new Date(2026, 9, 24, 15, 30).toISOString();

function coletor(nome: string, ativo = true, ultimoTokenEm: string | null = ULTIMO_TOKEN): Coletor {
  const chave = nome.toLowerCase().replace(/\s/g, '-');
  return {
    idPublico: `id-${chave}`,
    nome,
    clientId: `client-${chave}`,
    ativo,
    tenants: [
      { idPublico: 't-a', nome: 'Loja A' },
      { idPublico: 't-b', nome: 'Loja B' },
    ],
    criadoEm: '2026-10-01T10:00:00Z',
    revogadoEm: ativo ? null : '2026-10-25T10:00:00Z',
    ultimoTokenEm,
    versao: 'AAAAAAAAB9E=',
  };
}

function pagina(itens: Coletor[], total = itens.length): Paginado<Coletor> {
  return { itens, pagina: 1, tamanho: 20, total };
}

describe('ColetoresListaPage', () => {
  let fixture: ComponentFixture<ColetoresListaPage>;
  let http: HttpTestingController;

  async function criar(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [ColetoresListaPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ColetoresListaPage);
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  const tela = () => fixture.nativeElement as HTMLElement;
  const texto = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();
  /** Células de cada linha, separadas por espaço; na coluna de ações, o texto de cada botão/link. */
  const linhas = () =>
    [...tela().querySelectorAll('tbody tr')].map((tr) =>
      [...tr.querySelectorAll('td')]
        .map((td) => (td.querySelector('a, button') ? [...td.querySelectorAll('a, button')].map(texto).join(' ') : texto(td)))
        .join(' '),
    );
  const botao = (texto: string, raiz: ParentNode = tela()) =>
    [...raiz.querySelectorAll<HTMLElement>('button, a')].find((b) => b.textContent?.trim() === texto);
  const LISTA = '/api/admin/coletores?pagina=1';

  async function carregar(corpo: Paginado<Coletor>): Promise<void> {
    http.expectOne(LISTA).flush(corpo);
    await fixture.whenStable();
  }

  it('mostra Carregando e depois nome, clientId, situação, tenants e último token', async () => {
    await criar();
    expect(tela().querySelector('[role=status]')?.textContent?.trim()).toBe('Carregando...');

    await carregar(pagina([coletor('Servidor Matriz'), coletor('Servidor Filial', true, null)]));

    expect(tela().querySelector('[role=status]')).toBeNull();
    expect(linhas()).toEqual([
      'Servidor Matriz client-servidor-matriz Ativo Loja A, Loja B 24/10/2026 15:30 Editar Revogar Rotacionar',
      'Servidor Filial client-servidor-filial Ativo Loja A, Loja B Nunca Editar Revogar Rotacionar',
    ]);
  });

  it('lista vazia mostra o estado vazio com a ação Cadastrar coletor', async () => {
    await criar();
    await carregar(pagina([], 0));

    expect(tela().textContent).toContain('Nenhum coletor cadastrado.');
    expect(botao('Cadastrar coletor')?.getAttribute('href')).toBe('/admin/coletores/novo');
    expect(tela().querySelector('table')).toBeNull();
  });

  it('erro mostra a mensagem e Tentar novamente refaz a consulta', async () => {
    await criar();
    responderProblema(http.expectOne(LISTA), 500, 'ERRO_INTERNO', { traceId: 'abc123' });
    await fixture.whenStable();

    expect(tela().querySelector('[role=alert]')?.textContent).toContain('Erro inesperado (código abc123)');

    botao('Tentar novamente')!.click();
    await carregar(pagina([coletor('Servidor Matriz')]));
    expect(linhas()).toEqual([
      'Servidor Matriz client-servidor-matriz Ativo Loja A, Loja B 24/10/2026 15:30 Editar Revogar Rotacionar',
    ]);
  });

  it('revogar pede confirmação, chama a API e atualiza a linha', async () => {
    await criar();
    await carregar(pagina([coletor('Servidor Matriz'), coletor('Servidor Filial')]));

    botao('Revogar')!.click();
    await fixture.whenStable();
    const dialogo = tela().querySelector('[role=alertdialog]')!;
    expect(dialogo.textContent).toContain('Revogar credencial');
    expect(dialogo.textContent).toContain('Servidor Matriz deixará de enviar dados');
    http.expectNone('/api/admin/coletores/id-servidor-matriz/revogar');

    botao('Revogar', dialogo)!.click();
    const req = http.expectOne('/api/admin/coletores/id-servidor-matriz/revogar');
    expect(req.request.method).toBe('POST');
    req.flush(coletor('Servidor Matriz', false));
    await fixture.whenStable();

    expect(tela().querySelector('[role=alertdialog]')).toBeNull();
    expect(linhas()).toEqual([
      'Servidor Matriz client-servidor-matriz Revogado Loja A, Loja B 24/10/2026 15:30 Editar',
      'Servidor Filial client-servidor-filial Ativo Loja A, Loja B 24/10/2026 15:30 Editar Revogar Rotacionar',
    ]);
  });

  it('rotacionar pede confirmação e abre o diálogo com o novo secret', async () => {
    await criar();
    await carregar(pagina([coletor('Servidor Matriz')]));

    botao('Rotacionar')!.click();
    await fixture.whenStable();
    const confirmacao = tela().querySelector('[role=alertdialog]')!;
    expect(confirmacao.textContent).toContain('O secret atual de Servidor Matriz deixará de funcionar');
    http.expectNone('/api/admin/coletores/id-servidor-matriz/rotacionar');

    botao('Rotacionar', confirmacao)!.click();
    const req = http.expectOne('/api/admin/coletores/id-servidor-matriz/rotacionar');
    expect(req.request.method).toBe('POST');
    req.flush({ idPublico: 'id-servidor-matriz', clientId: 'client-servidor-matriz', clientSecret: 'segredo-novo-xyz' });
    await fixture.whenStable();

    expect(tela().querySelector('[role=alertdialog]')).toBeNull();
    const dialogo = tela().querySelector('[role=dialog]')!;
    expect(dialogo.querySelector<HTMLInputElement>('#segredo-client-id')!.value).toBe('client-servidor-matriz');
    expect(dialogo.querySelector<HTMLInputElement>('#segredo-client-secret')!.value).toBe('segredo-novo-xyz');
    expect(dialogo.textContent).toContain('não será exibido novamente');
  });

  it('COLETOR_REVOGADO ao rotacionar mostra o aviso e não abre o diálogo', async () => {
    await criar();
    await carregar(pagina([coletor('Servidor Matriz')]));

    botao('Rotacionar')!.click();
    await fixture.whenStable();
    botao('Rotacionar', tela().querySelector('[role=alertdialog]')!)!.click();
    responderProblema(http.expectOne('/api/admin/coletores/id-servidor-matriz/rotacionar'), 409, 'COLETOR_REVOGADO');
    await fixture.whenStable();

    expect(tela().querySelector('[role=alert]')?.textContent?.trim()).toBe('Credencial revogada');
    expect(tela().querySelector('[role=dialog]')).toBeNull();
  });
});
