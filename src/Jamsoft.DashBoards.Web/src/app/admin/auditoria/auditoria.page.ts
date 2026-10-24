import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { traduzirProblema } from '../../core/erros/problema';
import { EstadoCarga, Paginado } from '../../shared/paginado';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { PaginacaoComponent } from '../../shared/ui/paginacao.component';
import { AuditoriaApi, FiltrosAuditoria, RegistroAuditoria } from './auditoria.api';

/** Operações gravadas pela API (`OperacaoAuditada`). */
export const OPERACOES = [
  'ACESSO_CRUZADO',
  'LOGIN_SUPERADMIN',
  'ACESSO_CONTEXTO_TENANT',
  'TENANT_CRIADO',
  'TENANT_ALTERADO',
  'TENANT_INATIVADO',
  'TENANT_REATIVADO',
  'EMPRESA_CRIADA',
  'EMPRESA_ALTERADA',
  'USUARIO_CRIADO',
  'USUARIO_ALTERADO',
  'SENHA_REDEFINIDA',
  'EXCLUSAO_SOLICITADA',
  'EXCLUSAO_INICIADA',
  'EXCLUSAO_CONCLUIDA',
  'EXCLUSAO_FALHOU',
  'EXCLUSAO_RETOMADA',
] as const;

interface Consulta {
  filtros: FiltrosAuditoria;
  pagina: number;
}

type Resultado =
  | { estado: 'carregando' }
  | { estado: 'erro'; mensagem: string }
  | { estado: 'ok'; pagina: Paginado<RegistroAuditoria> };

/**
 * `/admin/auditoria` (AUD-03): filtros por período, tenant, usuário e operação, tabela paginada (mais recentes
 * primeiro) e detalhes expandíveis por linha.
 */
@Component({
  selector: 'app-auditoria',
  imports: [ReactiveFormsModule, DatePipe, EstadoCargaComponent, PaginacaoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .detalhes td {
      background: var(--surface-bg);
    }
    pre {
      margin: 0;
      white-space: pre-wrap;
      word-break: break-word;
      font-family: var(--font-mono);
      font-size: 11px;
    }
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 12px;
      margin: 0 0 8px;
      font-size: 12px;
    }
    dt {
      font-weight: 700;
    }
    dd {
      margin: 0;
    }
  `,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Administração</span>
          <h1>Auditoria</h1>
        </div>
      </div>

      <div class="card">
        <form class="barra-ferramentas" [formGroup]="filtros" (ngSubmit)="filtrar()">
          <div class="campo" style="flex: 0 1 160px">
            <label for="auditoria-de">De</label>
            <input id="auditoria-de" type="date" formControlName="de" />
          </div>
          <div class="campo" style="flex: 0 1 160px">
            <label for="auditoria-ate">Até</label>
            <input id="auditoria-ate" type="date" formControlName="ate" />
          </div>
          <div class="campo">
            <label for="auditoria-tenant">Tenant (ID público)</label>
            <input id="auditoria-tenant" formControlName="tenant" placeholder="00000000-0000-0000-0000-000000000000" />
          </div>
          <div class="campo">
            <label for="auditoria-usuario">Usuário (e-mail ou ID)</label>
            <input id="auditoria-usuario" formControlName="usuario" />
          </div>
          <div class="campo">
            <label for="auditoria-operacao">Operação</label>
            <select id="auditoria-operacao" formControlName="operacao">
              <option value="">Todas</option>
              @for (operacao of operacoes; track operacao) {
                <option [value]="operacao">{{ operacao }}</option>
              }
            </select>
          </div>
          <button type="submit" class="btn btn-primario">Filtrar</button>
          <button type="button" class="btn btn-contorno" (click)="limpar()">Limpar</button>
        </form>

        <app-estado-carga
          [estado]="estado()"
          textoVazio="Nenhum registro de auditoria para os filtros escolhidos."
          [mensagemErro]="mensagemErro()"
          (tentarNovamente)="recarregar()"
        />

        @if (estado() === 'pronto') {
          <div class="tabela-rolagem">
            <table class="tabela">
              <thead>
                <tr>
                  <th scope="col">Data/hora</th>
                  <th scope="col">Operação</th>
                  <th scope="col">Usuário</th>
                  <th scope="col">Tenant</th>
                  <th scope="col">Resultado</th>
                  <th scope="col"><span class="visualmente-oculto">Detalhes</span></th>
                </tr>
              </thead>
              <tbody>
                @for (registro of registros(); track registro.id) {
                  <tr>
                    <td class="mono">{{ registro.ocorridoEm | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
                    <td class="mono">{{ registro.operacao }}</td>
                    <td>{{ registro.usuarioEmail ?? registro.usuarioIdPublico ?? 'Sistema' }}</td>
                    <td>{{ registro.tenantNome ?? '—' }}</td>
                    <td>
                      <span class="badge" [class.badge-sucesso]="registro.resultado === 'Sucesso'"
                        [class.badge-perigo]="registro.resultado !== 'Sucesso'">{{ registro.resultado }}</span>
                    </td>
                    <td class="acoes">
                      <button type="button" class="btn btn-claro btn-sm" [attr.aria-expanded]="aberto() === registro.id"
                        [attr.aria-controls]="'auditoria-detalhes-' + registro.id" (click)="alternar(registro.id)">
                        {{ aberto() === registro.id ? 'Ocultar' : 'Detalhes' }}
                      </button>
                    </td>
                  </tr>
                  @if (aberto() === registro.id) {
                    <tr class="detalhes" [id]="'auditoria-detalhes-' + registro.id">
                      <td colspan="6">
                        <dl>
                          <dt>Perfil</dt><dd>{{ registro.perfil ?? '—' }}</dd>
                          <dt>Recurso</dt><dd class="mono">{{ registro.recursoIdPublico ?? '—' }}</dd>
                          <dt>Tenant</dt><dd class="mono">{{ registro.tenantIdPublico ?? '—' }}</dd>
                          <dt>IP</dt><dd class="mono">{{ registro.ip ?? '—' }}</dd>
                        </dl>
                        <pre>{{ detalhes(registro) }}</pre>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        }

        @if (pagina(); as p) {
          @if (p.total > 0) {
            <div class="barra-ferramentas">
              <app-paginacao [pagina]="p.pagina" [tamanho]="p.tamanho" [total]="p.total" (mudar)="irParaPagina($event)" />
            </div>
          }
        }
      </div>
    </section>
  `,
})
export class AuditoriaPage {
  private readonly api = inject(AuditoriaApi);

  readonly operacoes = OPERACOES;
  readonly filtros = inject(FormBuilder).nonNullable.group({ de: '', ate: '', tenant: '', usuario: '', operacao: '' });
  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly pagina = signal<Paginado<RegistroAuditoria> | null>(null);
  readonly registros = signal<RegistroAuditoria[]>([]);
  readonly aberto = signal<number | null>(null);

  private consulta: Consulta = { filtros: this.filtros.getRawValue(), pagina: 1 };
  private readonly carregar$ = new Subject<Consulta>();

  constructor() {
    this.carregar$
      .pipe(
        switchMap(({ filtros, pagina }) =>
          this.api.consultar(filtros, pagina).pipe(
            map((resultado): Resultado => ({ estado: 'ok', pagina: resultado })),
            catchError((erro: unknown) => of<Resultado>({ estado: 'erro', mensagem: traduzirProblema(erro).mensagem })),
            startWith<Resultado>({ estado: 'carregando' }),
          ),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((resultado) => this.aplicar(resultado));
    this.recarregar();
  }

  filtrar(): void {
    this.consultar({ filtros: this.filtros.getRawValue(), pagina: 1 });
  }

  limpar(): void {
    this.filtros.reset();
    this.filtrar();
  }

  irParaPagina(numero: number): void {
    this.consultar({ ...this.consulta, pagina: numero });
  }

  recarregar(): void {
    this.consultar(this.consulta);
  }

  alternar(id: number): void {
    this.aberto.update((atual) => (atual === id ? null : id));
  }

  /** JSON dos detalhes indentado; texto que não é JSON aparece como veio. */
  detalhes(registro: RegistroAuditoria): string {
    if (!registro.detalhes) return 'Sem detalhes.';
    try {
      return JSON.stringify(JSON.parse(registro.detalhes), null, 2);
    } catch {
      return registro.detalhes;
    }
  }

  private consultar(consulta: Consulta): void {
    this.consulta = consulta;
    this.aberto.set(null);
    this.carregar$.next(consulta);
  }

  private aplicar(resultado: Resultado): void {
    if (resultado.estado === 'carregando') {
      this.estado.set('carregando');
      return;
    }
    if (resultado.estado === 'erro') {
      this.mensagemErro.set(resultado.mensagem);
      this.pagina.set(null);
      this.estado.set('erro');
      return;
    }
    this.pagina.set(resultado.pagina);
    this.registros.set(resultado.pagina.itens);
    this.estado.set(resultado.pagina.itens.length === 0 ? 'vazio' : 'pronto');
  }
}
