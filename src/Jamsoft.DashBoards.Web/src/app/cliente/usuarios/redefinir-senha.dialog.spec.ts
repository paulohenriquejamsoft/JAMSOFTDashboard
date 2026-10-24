import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RedefinirSenhaDialog } from './redefinir-senha.dialog';
import { Usuario } from './usuarios.api';

const caio: Usuario = {
  idPublico: 'id-caio',
  nome: 'Caio Consulta',
  email: 'caio@loja.com',
  perfil: 'Consulta',
  ativo: true,
  trocarSenha: false,
  ultimoLoginEm: null,
  versao: 'AAAAAAAAB9E=',
};

describe('RedefinirSenhaDialog', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('envia a senha temporária, recebe 204 e confirma a redefinição', async () => {
    TestBed.configureTestingModule({
      imports: [RedefinirSenhaDialog],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RedefinirSenhaDialog);
    fixture.componentRef.setInput('usuario', caio);
    await fixture.whenStable();
    const tela = fixture.nativeElement as HTMLElement;
    const botao = (texto: string) => [...tela.querySelectorAll('button')].find((b) => b.textContent?.trim() === texto)!;

    const campo = tela.querySelector<HTMLInputElement>('#redefinir-senha')!;
    campo.value = 'Temporaria2026';
    campo.dispatchEvent(new Event('input'));
    botao('Redefinir').click();

    const req = http.expectOne({ method: 'POST', url: '/api/usuarios/id-caio/redefinir-senha' });
    expect(req.request.body).toEqual({ senhaTemporaria: 'Temporaria2026' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(tela.querySelector('[role=status]')?.textContent).toContain(
      'Senha de Caio Consulta redefinida. Ele precisará trocá-la no próximo acesso.',
    );
    expect(botao('Fechar')).toBeTruthy();
  });
});
