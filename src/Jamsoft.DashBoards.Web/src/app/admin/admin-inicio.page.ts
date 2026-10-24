import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Home do SuperAdmin (`/admin`): atalhos da área administrativa, no estilo "favoritos" do Shell. */
@Component({
  selector: 'app-admin-inicio',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .atalhos {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
      gap: 14px;
    }
    a {
      display: flex;
      flex-direction: column;
      gap: 3px;
      padding: 14px;
      text-decoration: none;
      color: var(--text-primary);
    }
    strong {
      font-size: 13px;
    }
    span {
      font-family: var(--font-mono);
      font-size: 9px;
      letter-spacing: 1.2px;
      color: var(--text-muted);
      text-transform: uppercase;
    }
  `,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Administração</span>
          <h1>Início</h1>
        </div>
      </div>
      <div class="atalhos">
        <a class="card" routerLink="/admin/tenants"><strong>Tenants</strong><span>Clientes da plataforma</span></a>
        <a class="card" routerLink="/admin/auditoria"><strong>Auditoria</strong><span>Operações críticas</span></a>
      </div>
    </section>
  `,
})
export class AdminInicioPage {}
