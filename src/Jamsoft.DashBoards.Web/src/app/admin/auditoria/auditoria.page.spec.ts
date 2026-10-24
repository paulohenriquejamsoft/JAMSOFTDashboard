import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { responderProblema } from '../../../testes/problemas';
import { Paginado } from '../../shared/paginado';
import { RegistroAuditoria } from './auditoria.api';
import { AuditoriaPage } from './auditoria.page';

function registro(id: number, operacao = 'TENANT_CRIADO'): RegistroAuditoria {
  return {
    id,
    ocorridoEm: '2026-10-24T15:30:00Z',
    usuarioIdPublico: 'u-1',
    usuarioEmail: 'root@jamsoft.com',
    perfil: 'SuperAdmin',
    operacao,
    recursoIdPublico: 'r-1',
    tenantIdPublico: 't-1',
    tenantNome: 'Loja A',
    ip: '10.0.0.1',
    resultado: 'Sucesso',
    detalhes: '{"cnpj":"11222333000181"}',
  };
}

function pagina(itens: RegistroAuditoria[], numero = 1, total = itens.length): Paginado<RegistroAuditoria> {
  return { itens, pagina: numero, tamanho: 20, total };
}

describe('AuditoriaPage', () => {
  let fixture: ComponentFixture<AuditoriaPage>;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [AuditoriaPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AuditoriaPage);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  const tela = () => fixture.nativeElement as HTMLElement;
  const botao = (texto: string) =>
    [...tela().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === texto);
  const pendente = () => http.expectOne((r) => r.url === '/api/admin/auditoria');

  async function responder(corpo: Paginado<RegistroAuditoria>): Promise<void> {
    pendente().flush(corpo);
    await fixture.whenStable();
  }

  function preencher(id: string, valor: string): void {
    const campo = tela().querySelector<HTMLInputElement | HTMLSelectElement>(`#${id}`)!;
    campo.value = valor;
    campo.dispatchEvent(new Event(campo instanceof HTMLSelectElement ? 'change' : 'input'));
  }

  it('filtros viram parâmetros da consulta, com o período em dias inteiros', async () => {
    await responder(pagina([registro(1)]));

    preencher('auditoria-de', '2026-10-01');
    preencher('auditoria-ate', '2026-10-24');
    preencher('auditoria-tenant', ' 3f2504e0-4f89-11d3-9a0c-0305e82c3301 ');
    preencher('auditoria-usuario', 'root@jamsoft.com');
    preencher('auditoria-operacao', 'SENHA_REDEFINIDA');
    botao('Filtrar')!.click();

    const req = pendente();
    const params = req.request.params;
    expect(params.get('pagina')).toBe('1');
    expect(params.get('de')).toBe(new Date(2026, 9, 1, 0, 0, 0, 0).toISOString());
    expect(params.get('ate')).toBe(new Date(2026, 9, 24, 23, 59, 59, 999).toISOString());
    expect(params.get('tenant')).toBe('3f2504e0-4f89-11d3-9a0c-0305e82c3301');
    expect(params.get('usuario')).toBe('root@jamsoft.com');
    expect(params.get('operacao')).toBe('SENHA_REDEFINIDA');
    req.flush(pagina([registro(2, 'SENHA_REDEFINIDA')]));
    await fixture.whenStable();
    expect(tela().querySelector('tbody tr')?.textContent).toContain('SENHA_REDEFINIDA');
  });

  it('paginação mantém os filtros e pede a página escolhida', async () => {
    await responder(pagina([registro(1)], 1, 60));
    preencher('auditoria-operacao', 'TENANT_CRIADO');
    botao('Filtrar')!.click();
    await responder(pagina([registro(1)], 1, 60));

    tela().querySelector<HTMLButtonElement>('button[aria-label="Página 2"]')!.click();

    const req = pendente();
    expect(req.request.params.get('pagina')).toBe('2');
    expect(req.request.params.get('operacao')).toBe('TENANT_CRIADO');
    req.flush(pagina([registro(21)], 2, 60));
    await fixture.whenStable();
    expect(tela().querySelector('button[aria-current=page]')?.textContent?.trim()).toBe('2');
  });

  it('sem registros mostra o estado vazio', async () => {
    await responder(pagina([], 1, 0));

    expect(tela().textContent).toContain('Nenhum registro de auditoria para os filtros escolhidos.');
    expect(tela().querySelector('table')).toBeNull();
  });

  it('erro mostra a mensagem e Tentar novamente repete a consulta', async () => {
    responderProblema(pendente(), 403, 'ACESSO_NEGADO');
    await fixture.whenStable();
    expect(tela().querySelector('[role=alert]')?.textContent).toContain('Acesso não permitido');

    botao('Tentar novamente')!.click();
    await responder(pagina([registro(1)]));
    expect(tela().querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('Detalhes expande a linha com perfil, recurso, IP e o JSON dos detalhes', async () => {
    await responder(pagina([registro(1)]));
    expect(tela().querySelector('#auditoria-detalhes-1')).toBeNull();

    botao('Detalhes')!.click();
    await fixture.whenStable();

    const detalhes = tela().querySelector('#auditoria-detalhes-1')!;
    expect(detalhes.textContent).toContain('SuperAdmin');
    expect(detalhes.textContent).toContain('10.0.0.1');
    expect(detalhes.querySelector('pre')?.textContent).toBe('{\n  "cnpj": "11222333000181"\n}');
    expect(botao('Ocultar')?.getAttribute('aria-expanded')).toBe('true');
  });
});
