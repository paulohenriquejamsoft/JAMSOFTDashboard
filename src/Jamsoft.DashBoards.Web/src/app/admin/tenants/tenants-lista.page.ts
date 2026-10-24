import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, finalize, map, of, startWith, switchMap } from 'rxjs';
import { traduzirProblema } from '../../core/erros/problema';
import { EstadoCarga, Paginado } from '../../shared/paginado';
import { ConfirmacaoDialog } from '../../shared/ui/confirmacao.dialog';
import { ExclusaoTenantDialog } from '../exclusao/exclusao-tenant.dialog';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { PaginacaoComponent } from '../../shared/ui/paginacao.component';
import { ConsultaTenants, SituacaoFiltro, Tenant, TenantsApi, formatarCnpj } from './tenants.api';

/** Espera depois da última tecla antes de consultar. */
export const DEBOUNCE_BUSCA_MS = 300;

type Resultado = { estado: 'carregando' } | { estado: 'erro'; mensagem: string } | { estado: 'ok'; pagina: Paginado<Tenant> };

/**
 * `/admin/tenants` (TEN-04, TEN-05, TEN-06): busca por nome/CNPJ, filtro de situação, paginação, estados de
 * carregamento, vazio ("Cadastrar tenant") e erro, e inativar/reativar com confirmação.
 */
@Component({
  selector: 'app-tenants-lista',
  imports: [RouterLink, EstadoCargaComponent, PaginacaoComponent, ConfirmacaoDialog, ExclusaoTenantDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Administração</span>
          <h1>Tenants</h1>
        </div>
        <a class="btn btn-sucesso" routerLink="/admin/tenants/novo">Novo tenant</a>
      </div>

      <div class="card">
        <div class="barra-ferramentas">
          <div class="campo">
            <label for="tenants-busca">Buscar</label>
            <input
              id="tenants-busca"
              type="search"
              placeholder="Nome ou CNPJ"
              [value]="consulta().busca"
              (input)="buscar($any($event.target).value)"
            />
          </div>
          <div class="campo" style="flex: 0 1 180px">
            <label for="tenants-situacao">Situação</label>
            <select id="tenants-situacao" (change)="filtrarSituacao($any($event.target).value)">
              <option value="">Todas</option>
              <option value="Ativo">Ativos</option>
              <option value="Inativo">Inativos</option>
            </select>
          </div>
        </div>

        @if (avisoAcao(); as aviso) {
          <p class="alerta alerta-erro" role="alert" style="margin: 0 16px 12px">{{ aviso }}</p>
        }

        <app-estado-carga
          [estado]="estado()"
          textoVazio="Nenhum tenant encontrado."
          [mensagemErro]="mensagemErro()"
          (tentarNovamente)="recarregar()"
        >
          <a acaoVazio class="btn btn-sucesso btn-sm" routerLink="/admin/tenants/novo">Cadastrar tenant</a>
        </app-estado-carga>

        @if (estado() === 'pronto') {
          <div class="tabela-rolagem">
            <table class="tabela">
              <thead>
                <tr>
                  <th scope="col">Nome</th>
                  <th scope="col">CNPJ</th>
                  <th scope="col">Situação</th>
                  <th scope="col"><span class="visualmente-oculto">Ações</span></th>
                </tr>
              </thead>
              <tbody>
                @for (tenant of itens(); track tenant.idPublico) {
                  <tr>
                    <td>{{ tenant.nome }}</td>
                    <td class="mono">{{ cnpj(tenant.cnpj) }}</td>
                    <td>
                      <span class="badge" [class.badge-sucesso]="tenant.ativo" [class.badge-neutro]="!tenant.ativo">
                        {{ tenant.ativo ? 'Ativo' : 'Inativo' }}
                      </span>
                    </td>
                    <td class="acoes">
                      <a class="btn btn-claro btn-sm" [routerLink]="['/admin/tenants', tenant.idPublico]">Editar</a>
                      <button type="button" class="btn btn-claro btn-sm" (click)="pedirSituacao(tenant)">
                        {{ tenant.ativo ? 'Inativar' : 'Reativar' }}
                      </button>
                      <button type="button" class="btn btn-suave btn-sm" (click)="alvoExclusao.set(tenant)">
                        Excluir
                      </button>
                    </td>
                  </tr>
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

    @if (alvoSituacao(); as alvo) {
      <app-confirmacao
        [titulo]="alvo.ativo ? 'Inativar tenant' : 'Reativar tenant'"
        [mensagem]="
          alvo.ativo
            ? 'Os usuários de ' + alvo.nome + ' não conseguirão entrar até a reativação. Os dados são preservados.'
            : 'Os usuários de ' + alvo.nome + ' voltarão a entrar.'
        "
        [textoConfirmar]="alvo.ativo ? 'Inativar' : 'Reativar'"
        [ocupado]="alterandoSituacao()"
        (confirmar)="confirmarSituacao(alvo)"
        (cancelar)="alvoSituacao.set(null)"
      />
    }

    @if (alvoExclusao(); as alvo) {
      <app-exclusao-tenant [tenant]="alvo" (fechado)="fecharExclusao($event)" />
    }
  `,
})
export class TenantsListaPage {
  private readonly api = inject(TenantsApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly consulta = signal<ConsultaTenants>({ busca: '', situacao: '', pagina: 1 });
  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly pagina = signal<Paginado<Tenant> | null>(null);
  readonly itens = signal<Tenant[]>([]);
  readonly alvoSituacao = signal<Tenant | null>(null);
  readonly alterandoSituacao = signal(false);
  readonly alvoExclusao = signal<Tenant | null>(null);
  readonly avisoAcao = signal<string | null>(null);

  private readonly busca$ = new Subject<string>();
  private readonly carregar$ = new Subject<ConsultaTenants>();

  constructor() {
    this.busca$
      .pipe(debounceTime(DEBOUNCE_BUSCA_MS), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((busca) => this.consultar({ ...this.consulta(), busca, pagina: 1 }));

    this.carregar$
      .pipe(
        switchMap((consulta) =>
          this.api.listar(consulta).pipe(
            map((pagina): Resultado => ({ estado: 'ok', pagina })),
            catchError((erro: unknown) => of<Resultado>({ estado: 'erro', mensagem: traduzirProblema(erro).mensagem })),
            startWith<Resultado>({ estado: 'carregando' }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((resultado) => this.aplicar(resultado));

    this.recarregar();
  }

  cnpj(valor: string): string {
    return formatarCnpj(valor);
  }

  buscar(texto: string): void {
    this.busca$.next(texto);
  }

  filtrarSituacao(situacao: SituacaoFiltro): void {
    this.consultar({ ...this.consulta(), situacao, pagina: 1 });
  }

  irParaPagina(numero: number): void {
    this.consultar({ ...this.consulta(), pagina: numero });
  }

  recarregar(): void {
    this.consultar(this.consulta());
  }

  pedirSituacao(tenant: Tenant): void {
    this.avisoAcao.set(null);
    this.alvoSituacao.set(tenant);
  }

  confirmarSituacao(tenant: Tenant): void {
    this.alterandoSituacao.set(true);
    const chamada = tenant.ativo ? this.api.inativar(tenant.idPublico) : this.api.reativar(tenant.idPublico);
    chamada.pipe(finalize(() => this.alterandoSituacao.set(false))).subscribe({
      next: (atualizado) => {
        this.itens.update((itens) => itens.map((t) => (t.idPublico === atualizado.idPublico ? atualizado : t)));
        this.alvoSituacao.set(null);
      },
      error: (erro: unknown) => {
        this.alvoSituacao.set(null);
        this.avisoAcao.set(traduzirProblema(erro).mensagem);
      },
    });
  }

  /** Depois de uma solicitação de exclusão o tenant fica inativo (ou some): a lista recarrega. */
  fecharExclusao(solicitou: boolean): void {
    this.alvoExclusao.set(null);
    if (solicitou) this.recarregar();
  }

  private consultar(consulta: ConsultaTenants): void {
    this.consulta.set(consulta);
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
    this.itens.set(resultado.pagina.itens);
    this.estado.set(resultado.pagina.itens.length === 0 ? 'vazio' : 'pronto');
  }
}
