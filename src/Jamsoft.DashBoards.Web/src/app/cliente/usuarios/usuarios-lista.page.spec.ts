import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { responderProblema } from '../../../testes/problemas';
import { AuthService, Perfil, UsuarioSessao } from '../../core/auth/auth.service';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';
import { PerfilTenant, Usuario } from './usuarios.api';
import { UsuariosListaPage } from './usuarios-lista.page';

function usuario(nome: string, perfil: PerfilTenant = 'Consulta', ativo = true): Usuario {
  const chave = nome.toLowerCase().replace(/\s/g, '.');
  return {
    idPublico: `id-${chave}`,
    nome,
    email: `${chave}@loja.com`,
    perfil,
    ativo,
    trocarSenha: false,
    ultimoLoginEm: null,
    versao: 'AAAAAAAAB9E=',
  };
}

describe('UsuariosListaPage', () => {
  let fixture: ComponentFixture<UsuariosListaPage>;
  let http: HttpTestingController;

  async function criar(perfil: Perfil): Promise<void> {
    const sessao: UsuarioSessao = {
      perfil,
      tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
      trocarSenha: false,
    };
    TestBed.configureTestingModule({
      imports: [UsuariosListaPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { usuario: signal(sessao).asReadonly() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UsuariosListaPage);
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
      [...tr.querySelectorAll('td')].map((td) => td.textContent!.replace(/\s+/g, ' ').trim()).join(' '),
    );
  const botaoEm = (raiz: Element, texto: string) =>
    [...raiz.querySelectorAll<HTMLElement>('button, a')].find((b) => b.textContent?.trim() === texto);

  async function carregar(usuarios: Usuario[]): Promise<void> {
    http.expectOne({ method: 'GET', url: '/api/usuarios' }).flush(usuarios);
    await fixture.whenStable();
  }

  it('renderiza nome, e-mail, perfil, situação e ações de cada usuário', async () => {
    await criar('Admin');
    await carregar([usuario('Ana Admin', 'Admin'), usuario('Caio Consulta', 'Consulta', false)]);

    expect(linhas()).toEqual([
      'Ana Admin ana.admin@loja.com Admin Ativo Editar Inativar Redefinir senha',
      'Caio Consulta caio.consulta@loja.com Consulta Inativo Editar Reativar Redefinir senha',
    ]);
    expect(botaoEm(tela(), 'Editar')?.getAttribute('href')).toBe('/cliente/usuarios/id-ana.admin');
  });

  it('inativar pede confirmação e envia o PUT com versao; a linha passa a Inativo', async () => {
    await criar('Admin');
    await carregar([usuario('Ana Admin', 'Admin'), usuario('Caio Consulta')]);

    botaoEm(tela().querySelectorAll('tbody tr')[1], 'Inativar')!.click();
    await fixture.whenStable();
    const dialogo = tela().querySelector('[role=alertdialog]')!;
    expect(dialogo.textContent).toContain('Inativar usuário');
    expect(dialogo.textContent).toContain('Caio Consulta perderá o acesso');
    http.expectNone('/api/usuarios/id-caio.consulta');

    botaoEm(dialogo, 'Inativar')!.click();
    const req = http.expectOne({ method: 'PUT', url: '/api/usuarios/id-caio.consulta' });
    expect(req.request.body).toEqual({ nome: 'Caio Consulta', perfil: 'Consulta', ativo: false, versao: 'AAAAAAAAB9E=' });
    req.flush(usuario('Caio Consulta', 'Consulta', false));
    await fixture.whenStable();

    expect(tela().querySelector('[role=alertdialog]')).toBeNull();
    expect(linhas()[1]).toBe('Caio Consulta caio.consulta@loja.com Consulta Inativo Editar Reativar Redefinir senha');
  });

  it('ULTIMO_ADMIN ao inativar o último Admin mostra a mensagem do spec e mantém a linha', async () => {
    await criar('Admin');
    await carregar([usuario('Ana Admin', 'Admin')]);

    botaoEm(tela(), 'Inativar')!.click();
    await fixture.whenStable();
    botaoEm(tela().querySelector('[role=alertdialog]')!, 'Inativar')!.click();
    responderProblema(http.expectOne({ method: 'PUT', url: '/api/usuarios/id-ana.admin' }), 422, 'ULTIMO_ADMIN');
    await fixture.whenStable();

    expect(tela().querySelector('[role=alert]')?.textContent?.trim()).toBe('O tenant precisa de ao menos um Admin ativo');
    expect(linhas()).toEqual(['Ana Admin ana.admin@loja.com Admin Ativo Editar Inativar Redefinir senha']);
  });

  it('SuperAdmin sem tenant vê o aviso e o seletor, sem consultar; ao escolher um tenant a lista carrega', async () => {
    await criar('SuperAdmin');

    expect(tela().querySelector('[role=status]')?.textContent?.trim()).toBe('Selecione um tenant');
    expect(tela().querySelector('app-seletor-tenant')?.textContent).toContain('Selecionar tenant');
    expect(tela().querySelector('table')).toBeNull();
    http.expectNone('/api/usuarios');

    TestBed.inject(TenantContextoService).selecionar({ idPublico: 'b2', nome: 'Loja B' });
    await fixture.whenStable();
    await carregar([usuario('Bia Admin', 'Admin')]);

    expect(tela().querySelector('app-seletor-tenant')).toBeNull();
    expect(linhas()).toEqual(['Bia Admin bia.admin@loja.com Admin Ativo Editar Inativar Redefinir senha']);
  });
});
