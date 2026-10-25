import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import type { Perfil, TenantResumo } from '../../core/auth/auth.service';
import { traduzirProblema } from '../../core/erros/problema';
import { ROTULO_PERFIL } from '../../shared/layout/shell.component';
import { EstadoCarga } from '../../shared/paginado';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';

/** Resposta de `GET /api/auth/eu` (`EuResposta`). */
interface Eu {
  idPublico: string;
  nome: string;
  email: string;
  perfil: Perfil;
  tenant: TenantResumo | null;
  trocarSenha: boolean;
}

/** `/conta/perfil` ("Meu perfil", UI-01): dados do usuário logado e o link para trocar a senha. */
@Component({
  selector: 'app-perfil',
  imports: [EstadoCargaComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    dl {
      margin: 0;
      display: grid;
      grid-template-columns: max-content minmax(0, 1fr);
      gap: var(--espaco-3) var(--espaco-5);
    }
    dt {
      font-family: var(--font-mono);
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 1.2px;
      text-transform: uppercase;
      color: var(--text-muted);
      align-self: center;
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .acoes {
      margin-top: var(--espaco-5);
    }
  `,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Conta</span>
          <h1>Meu perfil</h1>
        </div>
      </div>

      <div class="card">
        <app-estado-carga [estado]="estado()" [mensagemErro]="mensagemErro()" (tentarNovamente)="carregar()" />
        @if (estado() === 'pronto') {
          @if (eu(); as dados) {
            <div class="card-corpo">
              <dl>
                <dt>Nome</dt>
                <dd>{{ dados.nome }}</dd>
                <dt>E-mail</dt>
                <dd>{{ dados.email }}</dd>
                <dt>Perfil</dt>
                <dd>{{ rotuloPerfil(dados.perfil) }}</dd>
                <dt>Tenant</dt>
                <dd>{{ dados.tenant?.nome ?? '—' }}</dd>
              </dl>
              <div class="acoes">
                <a class="btn btn-contorno btn-sm" routerLink="/conta/trocar-senha">Trocar senha</a>
              </div>
            </div>
          }
        }
      </div>
    </section>
  `,
})
export class PerfilPage {
  private readonly http = inject(HttpClient);

  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly eu = signal<Eu | null>(null);
  private carga?: Subscription;

  constructor() {
    this.carregar();
  }

  rotuloPerfil(perfil: Perfil): string {
    return ROTULO_PERFIL[perfil] ?? perfil;
  }

  carregar(): void {
    this.carga?.unsubscribe();
    this.estado.set('carregando');
    this.carga = this.http.get<Eu>('/api/auth/eu').subscribe({
      next: (eu) => {
        this.eu.set(eu);
        this.estado.set('pronto');
      },
      error: (erro: unknown) => {
        this.mensagemErro.set(traduzirProblema(erro).mensagem);
        this.estado.set('erro');
      },
    });
  }
}
