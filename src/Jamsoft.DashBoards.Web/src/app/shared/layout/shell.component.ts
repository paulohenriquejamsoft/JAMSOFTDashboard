import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService, Perfil } from '../../core/auth/auth.service';
import { EstadoAviso } from '../../core/auth/perfil.guard';
import { TenantContextoService } from '../../core/auth/tenant-contexto.service';

interface ItemMenu {
  rotulo: string;
  link: string;
  /** `exact` para a home da área não ficar ativa em todas as subrotas. */
  exato?: boolean;
}

interface SecaoMenu {
  titulo: string;
  itens: ItemMenu[];
}

const ROTULO_PERFIL: Record<Perfil, string> = {
  SuperAdmin: 'SuperAdmin',
  Admin: 'Administrador',
  Consulta: 'Consulta',
};

/**
 * Layout logado do Design System ("Shell"): menu lateral com o gradiente vermelho e a logo branca, header de
 * 58px com o usuário e "Sair". O menu muda com o perfil; a API revalida tudo (AD-008).
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './shell.component.scss',
  template: `
    <nav class="menu" aria-label="Menu principal">
      <div class="menu-topo">
        <img src="marca/logo-branca.png" alt="JAMSOFT Sistemas" />
      </div>
      @for (secao of secoes(); track secao.titulo) {
        <p class="menu-secao">{{ secao.titulo }}</p>
        @for (item of secao.itens; track item.link) {
          <a
            class="menu-item"
            [routerLink]="item.link"
            routerLinkActive="ativo"
            [routerLinkActiveOptions]="{ exact: !!item.exato }"
            ariaCurrentWhenActive="page"
            >{{ item.rotulo }}</a
          >
        }
      }
    </nav>

    <div class="area">
      <header class="cabecalho">
        <ng-content select="[cabecalho]" />
        <div class="usuario">
          <span class="avatar" aria-hidden="true">{{ iniciais() }}</span>
          <div class="usuario-texto">
            <span class="usuario-nome">{{ rotuloPerfil() }}</span>
            @if (usuario()?.tenant; as tenant) {
              <span class="usuario-tenant">{{ tenant.nome }}</span>
            }
          </div>
          <button type="button" class="btn btn-suave btn-sm" (click)="sair()">Sair</button>
        </div>
      </header>

      <main class="conteudo">
        @if (aviso(); as texto) {
          <p class="alerta alerta-atencao" role="status">{{ texto }}</p>
        }
        <router-outlet />
      </main>
    </div>
  `,
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly contexto = inject(TenantContextoService);
  private readonly router = inject(Router);

  readonly usuario = this.auth.usuario;
  readonly rotuloPerfil = computed(() => {
    const perfil = this.usuario()?.perfil;
    return perfil ? ROTULO_PERFIL[perfil] : '';
  });
  readonly iniciais = computed(() => this.rotuloPerfil().slice(0, 2).toUpperCase());

  /** Aviso deixado pelo `perfilGuard` ao redirecionar para a home (acesso negado, tenant não selecionado). */
  readonly aviso = computed(() => {
    const estado = this.router.lastSuccessfulNavigation()?.extras.state as Partial<EstadoAviso> | undefined;
    return estado?.aviso ?? null;
  });

  readonly secoes = computed<SecaoMenu[]>(() => {
    const perfil = this.usuario()?.perfil;
    const tenant: ItemMenu[] = [
      { rotulo: 'Painel', link: '/cliente', exato: true },
      { rotulo: 'Usuários', link: '/cliente/usuarios' },
      { rotulo: 'Empresas', link: '/cliente/empresas' },
    ];
    if (perfil === 'SuperAdmin') {
      const secoes: SecaoMenu[] = [
        {
          titulo: 'Administração',
          itens: [
            { rotulo: 'Início', link: '/admin', exato: true },
            { rotulo: 'Tenants', link: '/admin/tenants' },
            { rotulo: 'Auditoria', link: '/admin/auditoria' },
          ],
        },
      ];
      const selecionado = this.contexto.selecionado();
      if (selecionado) secoes.push({ titulo: selecionado.nome, itens: tenant });
      return secoes;
    }
    if (perfil === 'Admin') return [{ titulo: 'Principal', itens: tenant }];
    return [{ titulo: 'Principal', itens: tenant.slice(0, 1) }];
  });

  sair(): void {
    this.auth.logout().subscribe({ complete: () => void this.router.navigateByUrl('/conta/login') });
  }
}
