import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { AVISO_SELECIONE_TENANT, EstadoAviso } from '../../core/auth/perfil.guard';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';
import { traduzirProblema } from '../../core/erros/problema';
import { Tenant, TenantsApi } from '../tenants/tenants.api';

export const DEBOUNCE_SELETOR_MS = 300;
const RESULTADOS = 10;

/**
 * Seletor de tenant do SuperAdmin no cabeçalho (CTX-01): busca tenants, grava a escolha no
 * {@link TenantContextoService} (que vira `X-Tenant`) e mostra o nome. Abre sozinho quando o `perfilGuard`
 * barra uma rota do tenant por falta de seleção. Invisível para Admin e Consulta.
 */
@Component({
  selector: 'app-seletor-tenant',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      position: relative;
      display: block;
    }
    .atual {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
    }
    .rotulo {
      font-family: var(--font-mono);
      font-size: 9px;
      font-weight: 700;
      letter-spacing: 1.2px;
      color: var(--text-muted);
      text-transform: uppercase;
    }
    .painel {
      position: absolute;
      top: calc(100% + 8px);
      left: 0;
      z-index: 40;
      width: 320px;
      padding: 12px;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      max-height: 260px;
      overflow: auto;
    }
    li button {
      width: 100%;
      text-align: left;
      border: 0;
      background: none;
      padding: 8px;
      border-radius: 6px;
      font: inherit;
      font-size: 13px;
      cursor: pointer;
      color: var(--text-primary);
    }
    li button:hover,
    li button:focus-visible {
      background: var(--primary-soft);
      outline: none;
    }
    .vazio {
      font-size: 12px;
      color: var(--text-muted);
      margin: 0;
    }
  `,
  template: `
    @if (visivel()) {
      <div class="atual">
        <span class="rotulo">Tenant</span>
        @if (contexto.selecionado(); as tenant) {
          <strong data-teste="tenant-atual">{{ tenant.nome }}</strong>
          <button type="button" class="btn btn-claro btn-sm" (click)="limpar()">Limpar</button>
        } @else {
          <span class="vazio">Nenhum selecionado</span>
        }
        <button
          type="button"
          class="btn btn-suave btn-sm"
          [attr.aria-expanded]="aberto()"
          aria-controls="seletor-tenant-painel"
          (click)="alternar()"
        >
          {{ contexto.selecionado() ? 'Trocar' : 'Selecionar tenant' }}
        </button>
      </div>

      @if (aberto()) {
        <div id="seletor-tenant-painel" class="painel card" role="dialog" aria-label="Selecionar tenant">
          <div class="campo">
            <label for="seletor-tenant-busca">Buscar tenant</label>
            <input
              id="seletor-tenant-busca"
              type="search"
              placeholder="Nome ou CNPJ"
              (input)="buscar($any($event.target).value)"
            />
          </div>
          @if (erro(); as mensagem) {
            <p class="campo-erro" role="alert">{{ mensagem }}</p>
          } @else if (resultados().length === 0) {
            <p class="vazio">Nenhum tenant encontrado.</p>
          } @else {
            <ul>
              @for (tenant of resultados(); track tenant.idPublico) {
                <li>
                  <button type="button" (click)="escolher(tenant)">
                    {{ tenant.nome }}{{ tenant.ativo ? '' : ' (inativo)' }}
                  </button>
                </li>
              }
            </ul>
          }
        </div>
      }
    }
  `,
})
export class SeletorTenantComponent {
  private readonly api = inject(TenantsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly contexto = inject(TenantContextoService);

  readonly visivel = computed(() => this.auth.usuario()?.perfil === 'SuperAdmin');
  readonly aberto = signal(false);
  readonly resultados = signal<Tenant[]>([]);
  readonly erro = signal<string | null>(null);
  private readonly busca$ = new Subject<string>();

  constructor() {
    this.busca$
      .pipe(
        debounceTime(DEBOUNCE_SELETOR_MS),
        switchMap((busca) =>
          this.api.listar({ busca, situacao: '', pagina: 1, tamanho: RESULTADOS }).pipe(
            map((pagina) => ({ itens: pagina.itens, erro: null as string | null })),
            catchError((erro: unknown) => of({ itens: [] as Tenant[], erro: traduzirProblema(erro).mensagem })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ itens, erro }) => {
        this.resultados.set(itens);
        this.erro.set(erro);
      });

    // Rota do tenant barrada por falta de seleção: o guard redireciona com este aviso e o seletor abre.
    effect(() => {
      const estado = this.router.lastSuccessfulNavigation()?.extras.state as Partial<EstadoAviso> | undefined;
      if (this.visivel() && estado?.aviso === AVISO_SELECIONE_TENANT && !this.contexto.selecionado()) this.abrir();
    });
  }

  alternar(): void {
    if (this.aberto()) this.aberto.set(false);
    else this.abrir();
  }

  buscar(texto: string): void {
    this.busca$.next(texto);
  }

  escolher(tenant: Tenant): void {
    this.contexto.selecionar({ idPublico: tenant.idPublico, nome: tenant.nome });
    this.aberto.set(false);
  }

  limpar(): void {
    this.contexto.limpar();
  }

  private abrir(): void {
    if (this.aberto()) return;
    this.aberto.set(true);
    this.busca$.next('');
  }
}
