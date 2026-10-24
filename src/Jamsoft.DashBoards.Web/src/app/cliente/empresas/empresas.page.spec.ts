import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { AuthService, UsuarioSessao } from '../../core/auth/auth.service';
import { Empresa } from './empresas.api';
import { EmpresasPage } from './empresas.page';

function empresa(codEmpresa: number, nome: string, nomeManual = false): Empresa {
  return { idPublico: `id-${codEmpresa}`, codEmpresa, nome, nomeManual };
}

describe('EmpresasPage', () => {
  let fixture: ComponentFixture<EmpresasPage>;
  let http: HttpTestingController;

  async function criar(): Promise<void> {
    const sessao: UsuarioSessao = { perfil: 'Admin', tenant: { idPublico: 'a1', nome: 'Loja A' }, trocarSenha: false };
    TestBed.configureTestingModule({
      imports: [EmpresasPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { usuario: signal(sessao).asReadonly() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmpresasPage);
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  const tela = () => fixture.nativeElement as HTMLElement;
  const linhas = () =>
    [...tela().querySelectorAll('tbody tr')].map((tr) =>
      [...tr.querySelectorAll('td')].map((td) => td.textContent!.replace(/\s+/g, ' ').trim()).join(' '),
    );
  const botao = (texto: string) =>
    [...tela().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === texto)!;

  function digitar(id: string, valor: string): void {
    const campo = tela().querySelector<HTMLInputElement>(`#${id}`)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  }

  async function carregar(empresas: Empresa[]): Promise<void> {
    http.expectOne({ method: 'GET', url: '/api/empresas' }).flush(empresas);
    await fixture.whenStable();
  }

  async function cadastrar(codigo: string, nome: string): Promise<void> {
    digitar('empresa-codigo', codigo);
    digitar('empresa-nome', nome);
    botao('Cadastrar').click();
    await fixture.whenStable();
  }

  it('lista código e nome das empresas do tenant', async () => {
    await criar();
    await carregar([empresa(1, 'Matriz'), empresa(7, 'Filial Centro', true)]);

    expect(linhas()).toEqual(['1 Matriz Renomear', '7 Filial Centro Renomear']);
  });

  it('cadastro válido envia código e nome e acrescenta a linha na ordem do código', async () => {
    await criar();
    await carregar([empresa(1, 'Matriz'), empresa(9, 'Filial Norte')]);

    await cadastrar('5', ' Filial Sul ');
    const req = http.expectOne({ method: 'POST', url: '/api/empresas' });
    expect(req.request.body).toEqual({ codEmpresa: 5, nome: 'Filial Sul' });
    req.flush(empresa(5, 'Filial Sul', true), { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    expect(linhas()).toEqual(['1 Matriz Renomear', '5 Filial Sul Renomear', '9 Filial Norte Renomear']);
    expect(tela().querySelector<HTMLInputElement>('#empresa-codigo')!.value).toBe('');
  });

  it('EMPRESA_DUPLICADA aparece no campo código', async () => {
    await criar();
    await carregar([empresa(1, 'Matriz')]);

    await cadastrar('1', 'Outra Matriz');
    responderProblema(http.expectOne({ method: 'POST', url: '/api/empresas' }), 409, 'EMPRESA_DUPLICADA');
    await fixture.whenStable();

    expect(tela().querySelector('#empresa-codigo-erro')?.textContent?.trim()).toBe('Já existe empresa com este código');
    expect(tela().querySelector('#empresa-codigo')?.getAttribute('aria-invalid')).toBe('true');
    expect(linhas()).toEqual(['1 Matriz Renomear']);
  });

  it('renomear envia o novo nome e atualiza a linha', async () => {
    await criar();
    await carregar([empresa(1, 'Matriz'), empresa(2, 'LOJA 02 ERP')]);

    tela().querySelectorAll('tbody tr')[1].querySelector('button')!.click();
    await fixture.whenStable();
    digitar('empresa-renomear', 'Loja Shopping');
    botao('Salvar').click();

    const req = http.expectOne({ method: 'PUT', url: '/api/empresas/id-2' });
    expect(req.request.body).toEqual({ nome: 'Loja Shopping' });
    req.flush(empresa(2, 'Loja Shopping', true));
    await fixture.whenStable();

    expect(linhas()).toEqual(['1 Matriz Renomear', '2 Loja Shopping Renomear']);
  });

  it('lista vazia mostra o estado vazio; erro mostra a mensagem e Tentar novamente refaz a consulta', async () => {
    await criar();
    await carregar([]);
    expect(tela().textContent).toContain('Nenhuma empresa cadastrada.');
    expect(tela().querySelector('table')).toBeNull();

    fixture.componentInstance.recarregar();
    responderProblema(http.expectOne('/api/empresas'), 500, 'ERRO_INTERNO', { traceId: 'xyz789' });
    await fixture.whenStable();
    expect(tela().querySelector('[role=alert]')?.textContent).toContain('Erro inesperado (código xyz789)');

    botao('Tentar novamente').click();
    await carregar([empresa(3, 'Filial Leste')]);
    expect(linhas()).toEqual(['3 Filial Leste Renomear']);
  });
});
