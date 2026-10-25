import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { responderProblema } from '../../../testes/problemas';
import { AuthService, Perfil, UsuarioSessao } from '../../core/auth/auth.service';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';
import { RELOGIO, StatusDadosComponent } from './status-dados.component';

const HORA = 60 * 60 * 1000;
/** "Agora" fixo do relógio injetado. */
const AGORA = new Date(2026, 9, 25, 16, 30).getTime();
const RESUMO = '/api/sincronizacao/resumo';

describe('StatusDadosComponent', () => {
  let fixture: ComponentFixture<StatusDadosComponent>;
  let http: HttpTestingController;

  async function criar(perfil: Perfil): Promise<void> {
    const sessao: UsuarioSessao = {
      perfil,
      tenant: perfil === 'SuperAdmin' ? null : { idPublico: 'a1', nome: 'Loja A' },
      trocarSenha: false,
    };
    TestBed.configureTestingModule({
      imports: [StatusDadosComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { usuario: signal(sessao).asReadonly() } },
        { provide: RELOGIO, useValue: () => AGORA },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StatusDadosComponent);
    await fixture.whenStable();
  }

  async function responder(ultimoSucessoEm: string | null): Promise<void> {
    http.expectOne({ method: 'GET', url: RESUMO }).flush({ ultimoSucessoEm });
    await fixture.whenStable();
  }

  const haAtras = (horas: number) => new Date(AGORA - horas * HORA).toISOString();
  const texto = () => (fixture.nativeElement as HTMLElement).textContent!.replace(/\s+/g, ' ').trim();
  const alerta = () => (fixture.nativeElement as HTMLElement).querySelector('[role=alert]');

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('mostra "Dados atualizados em" com a data formatada dd/MM/yyyy HH:mm', async () => {
    await criar('Consulta');
    await responder(new Date(2026, 9, 25, 9, 5).toISOString());

    expect(texto()).toBe('Dados atualizados em 25/10/2026 09:05');
    expect(alerta()).toBeNull();
  });

  it('25 h sem sincronizar mostra o alerta "Dados desatualizados" para Admin', async () => {
    await criar('Admin');
    await responder(haAtras(25));

    expect(texto()).toContain('Dados atualizados em');
    expect(alerta()?.textContent?.trim()).toBe('Dados desatualizados');
  });

  it('25 h sem sincronizar não mostra o alerta para Consulta, só a data', async () => {
    await criar('Consulta');
    await responder(haAtras(25));

    expect(texto()).toContain('Dados atualizados em');
    expect(alerta()).toBeNull();
    expect(texto()).not.toContain('Dados desatualizados');
  });

  it('23 h sem sincronizar não mostra o alerta para Admin', async () => {
    await criar('Admin');
    await responder(haAtras(23));

    expect(texto()).toContain('Dados atualizados em');
    expect(alerta()).toBeNull();
  });

  it('sem sincronização mostra "Nenhuma sincronização ainda"', async () => {
    await criar('Admin');
    await responder(null);

    expect(texto()).toBe('Nenhuma sincronização ainda');
    expect(alerta()).toBeNull();
  });

  it('SuperAdmin sem tenant não consulta; com tenant selecionado consulta e vê o alerta de 25 h', async () => {
    await criar('SuperAdmin');
    http.expectNone(RESUMO);
    expect(texto()).toBe('');

    TestBed.inject(TenantContextoService).selecionar({ idPublico: 'b2', nome: 'Loja B' });
    await fixture.whenStable();
    await responder(haAtras(25));

    expect(alerta()?.textContent?.trim()).toBe('Dados desatualizados');
  });

  it('falha ao consultar o resumo não mostra nada no cabeçalho', async () => {
    await criar('Admin');
    responderProblema(http.expectOne(RESUMO), 500, 'ERRO_INTERNO');
    await fixture.whenStable();

    expect(texto()).toBe('');
  });
});
