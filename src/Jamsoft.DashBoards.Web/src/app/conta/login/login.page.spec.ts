import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { LoginPage } from './login.page';

describe('LoginPage', () => {
  let fixture: ComponentFixture<LoginPage>;
  let http: HttpTestingController;
  let navegar: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const tela = () => fixture.nativeElement as HTMLElement;
  const botao = () => tela().querySelector<HTMLButtonElement>('button[type=submit]')!;

  function digitar(id: string, valor: string): void {
    const campo = tela().querySelector<HTMLInputElement>(`#${id}`)!;
    campo.value = valor;
    campo.dispatchEvent(new Event('input'));
  }

  async function enviar(email = 'admin@loja.com', senha = 'SenhaForte123'): Promise<void> {
    digitar('login-email', email);
    digitar('login-senha', senha);
    botao().click();
    await fixture.whenStable();
  }

  function sessao(perfil: string, trocarSenha = false) {
    return {
      accessToken: 'token',
      expiraEm: '2026-10-24T12:15:00Z',
      perfil,
      tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
      trocarSenha,
    };
  }

  it('campos vazios mostram as mensagens e não chamam a API', async () => {
    await enviar('', '');

    expect(tela().querySelector('#login-email-erro')?.textContent).toContain('Informe o e-mail.');
    expect(tela().querySelector('#login-senha-erro')?.textContent).toContain('Informe a senha.');
    expect(tela().querySelector('#login-email')?.getAttribute('aria-invalid')).toBe('true');
    http.expectNone('/api/auth/login');
  });

  it('botão fica desabilitado enquanto envia e volta depois da resposta', async () => {
    await enviar();

    const req = http.expectOne('/api/auth/login');
    expect(req.request.body).toEqual({ email: 'admin@loja.com', senha: 'SenhaForte123' });
    expect(botao().disabled).toBe(true);
    expect(botao().textContent).toContain('Entrando...');

    responderProblema(req, 401, 'CREDENCIAIS_INVALIDAS');
    await fixture.whenStable();
    expect(botao().disabled).toBe(false);
  });

  it.each([
    [401, 'CREDENCIAIS_INVALIDAS', {}, 'Credenciais inválidas'],
    [
      423,
      'USUARIO_BLOQUEADO',
      { bloqueadoAte: new Date(2026, 9, 24, 14, 35).toISOString() },
      'Muitas tentativas. Tente novamente às 14:35',
    ],
    [403, 'ACESSO_DESATIVADO', {}, 'Acesso desativado'],
    [429, 'MUITAS_REQUISICOES', {}, 'Aguarde um minuto'],
  ])('erro %i %s mostra a mensagem certa', async (status, codigo, extras, mensagem) => {
    await enviar();

    responderProblema(http.expectOne('/api/auth/login'), status, codigo, extras);
    await fixture.whenStable();

    expect(tela().querySelector('[role=alert]')?.textContent?.trim()).toBe(mensagem);
    expect(navegar).not.toHaveBeenCalled();
  });

  it.each([
    ['SuperAdmin', false, '/admin/tenants'],
    ['Admin', false, '/cliente'],
    ['Consulta', false, '/cliente'],
    ['Admin', true, '/conta/trocar-senha'],
  ])('sucesso de %s (troca pendente: %s) leva a %s', async (perfil, trocarSenha, destino) => {
    await enviar();

    http.expectOne('/api/auth/login').flush(sessao(perfil, trocarSenha));
    await fixture.whenStable();

    expect(navegar).toHaveBeenCalledWith(destino);
  });
});
