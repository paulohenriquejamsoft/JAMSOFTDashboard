import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AtualizacaoService } from './core/pwa/atualizacao.service';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .faixa-atualizacao {
      position: fixed;
      left: 50%;
      bottom: 16px;
      transform: translateX(-50%);
      z-index: 60;
      width: max-content;
      max-width: calc(100% - 32px);
      display: flex;
      align-items: center;
      gap: 12px;
      margin: 0;
      box-shadow: var(--sombra-card);
    }
  `,
  template: `
    <router-outlet />
    @if (atualizacao.disponivel()) {
      <div class="alerta alerta-atencao faixa-atualizacao" role="status">
        <span>Nova versão disponível</span>
        <button type="button" class="btn btn-primario btn-sm" (click)="atualizacao.recarregar()">Recarregar</button>
      </div>
    }
  `,
})
export class App {
  readonly atualizacao = inject(AtualizacaoService);
}
