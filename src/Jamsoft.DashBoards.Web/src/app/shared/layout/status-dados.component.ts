import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, InjectionToken, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { tenantEfetivo } from '../../core/auth/tenant-efetivo';
import { ResumoSincronizacao, SincronizacaoApi } from '../../cliente/sincronizacao/sincronizacao.api';

/** Relógio em milissegundos; trocado nos testes. */
export const RELOGIO = new InjectionToken<() => number>('RELOGIO', { providedIn: 'root', factory: () => Date.now });

/** Acima disso os dados contam como desatualizados (STS-03). */
export const LIMITE_DESATUALIZADO_MS = 24 * 60 * 60 * 1000;

/**
 * Status dos dados no cabeçalho do shell (STS-02, STS-03): "Dados atualizados em dd/MM/yyyy HH:mm" para todos os
 * perfis, "Nenhuma sincronização ainda" sem dados e, só para Admin e SuperAdmin, o alerta "Dados desatualizados"
 * quando a última sincronização bem-sucedida passou de 24 h. Sem tenant efetivo ou com falha na consulta, some.
 */
@Component({
  selector: 'app-status-dados',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: var(--text-muted);
    }
  `,
  template: `
    @if (resumo(); as r) {
      @if (r.ultimoSucessoEm; as data) {
        <span>Dados atualizados em {{ data | date: 'dd/MM/yyyy HH:mm' }}</span>
        @if (desatualizado()) {
          <span class="badge badge-atencao" role="alert">Dados desatualizados</span>
        }
      } @else {
        <span>Nenhuma sincronização ainda</span>
      }
    }
  `,
})
export class StatusDadosComponent {
  private readonly api = inject(SincronizacaoApi);
  private readonly auth = inject(AuthService);
  private readonly agora = inject(RELOGIO);

  readonly tenant = tenantEfetivo();
  readonly resumo = signal<ResumoSincronizacao | null>(null);
  /** Momento da última consulta, para comparar com a data do resumo. */
  private readonly consultadoEm = signal(0);
  private carga?: Subscription;

  readonly desatualizado = computed(() => {
    const perfil = this.auth.usuario()?.perfil;
    const data = this.resumo()?.ultimoSucessoEm;
    if (!data || (perfil !== 'Admin' && perfil !== 'SuperAdmin')) return false;
    return this.consultadoEm() - new Date(data).getTime() > LIMITE_DESATUALIZADO_MS;
  });

  constructor() {
    // Consulta de novo quando o tenant efetivo muda (SuperAdmin troca a seleção no cabeçalho).
    effect(() => {
      const tenant = this.tenant();
      untracked(() => this.carregar(tenant));
    });
  }

  private carregar(tenant: string | null): void {
    this.carga?.unsubscribe();
    this.resumo.set(null);
    if (!tenant) return;
    this.carga = this.api.resumo().subscribe({
      next: (resumo) => {
        this.consultadoEm.set(this.agora());
        this.resumo.set(resumo);
      },
      error: () => this.resumo.set(null),
    });
  }
}
