import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { AuthService } from '../../core/auth/auth.service';
import { TrocarSenhaPage } from './trocar-senha.page';

describe('TrocarSenhaPage', () => {
  let fixture: ComponentFixture<TrocarSenhaPage>;
  let http: HttpTestingController;
  let navegar: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [TrocarSenhaPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(TrocarSenhaPage);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  const tela = () => fixture.nativeElement as HTMLElement;

  async function preencherEEnviar(atual: string, nova: string, confirmacao: string): Promise<void> {
    for (const [id, valor] of [
      ['senha-atual', atual],
      ['senha-nova', nova],
      ['senha-confirmacao', confirmacao],
    ]) {
      const campo = tela().querySelector<HTMLInputElement>(`#${id}`)!;
      campo.value = valor;
      campo.dispatchEvent(new Event('input'));
    }
    tela().querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await fixture.whenStable();
  }

  it('confirmação diferente bloqueia o envio', async () => {
    await preencherEEnviar('SenhaAntiga1', 'SenhaNova1234', 'SenhaNova9999');

    expect(tela().querySelector('#senha-confirmacao-erro')?.textContent).toContain(
      'A confirmação não confere com a nova senha.',
    );
    http.expectNone('/api/auth/trocar-senha');
  });

  it('senha fora da política mostra as regras violadas e não envia', async () => {
    await preencherEEnviar('SenhaAntiga1', 'curta', 'curta');

    const regras = [...tela().querySelectorAll('#senha-nova-regras li')].map((li) => li.textContent?.trim());
    expect(regras).toEqual([
      'A senha precisa ter ao menos 10 caracteres.',
      'A senha precisa ter ao menos um dígito.',
    ]);
    http.expectNone('/api/auth/trocar-senha');
  });

  it('senha atual errada mostra o erro da API no campo', async () => {
    await preencherEEnviar('Errada12345', 'SenhaNova1234', 'SenhaNova1234');

    const req = http.expectOne('/api/auth/trocar-senha');
    expect(req.request.body).toEqual({ senhaAtual: 'Errada12345', novaSenha: 'SenhaNova1234' });
    responderProblema(req, 400, 'VALIDACAO', { erros: { senhaAtual: ['Senha atual incorreta.'] } });
    await fixture.whenStable();

    expect(tela().querySelector('#senha-atual-erro')?.textContent).toContain('Senha atual incorreta.');
    expect(navegar).not.toHaveBeenCalled();
  });

  it('sucesso abre a sessão nova e leva à home do perfil', async () => {
    await preencherEEnviar('SenhaAntiga1', 'SenhaNova1234', 'SenhaNova1234');

    http.expectOne('/api/auth/trocar-senha').flush({
      accessToken: 'token-novo',
      expiraEm: '2026-10-24T12:15:00Z',
      perfil: 'Admin',
      tenant: { idPublico: 'a1', nome: 'Loja A' },
      trocarSenha: false,
    });
    await fixture.whenStable();

    expect(navegar).toHaveBeenCalledWith('/cliente');
    const auth = TestBed.inject(AuthService);
    expect(auth.accessToken).toBe('token-novo');
    expect(auth.usuario()?.trocarSenha).toBe(false);
  });
});
