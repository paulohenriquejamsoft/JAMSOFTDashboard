import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { environment } from '../../../environments/environment';

export const AVISO_CHAVE = 'Guarde a chave agora: ela não será exibida novamente.';

/**
 * Mostra a api-key uma única vez, logo depois de criar, com a URL do servidor MCP, botões de copiar e onde colar no
 * Claude. Quem abre guarda a chave só enquanto o diálogo está aberto e a descarta em `fechado`.
 */
@Component({
  selector: 'app-chave-mcp-criada',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'fechado.emit()' },
  template: `
    <div class="dialogo-fundo">
      <div class="dialogo" role="dialog" aria-modal="true" aria-labelledby="chave-mcp-titulo">
        <div class="dialogo-cabecalho">
          <h2 id="chave-mcp-titulo">Chave do conector Claude</h2>
        </div>
        <div class="dialogo-corpo">
          <p class="alerta alerta-atencao" role="status">{{ aviso }}</p>
          <div class="campo">
            <label for="chave-mcp-url">URL do servidor MCP</label>
            <input id="chave-mcp-url" class="mono" readonly [value]="url" />
            <button type="button" class="btn btn-claro btn-sm" aria-label="Copiar URL" (click)="copiar('url', url)">Copiar</button>
          </div>
          <div class="campo">
            <label for="chave-mcp-valor">Api-key</label>
            <input id="chave-mcp-valor" class="mono" readonly [value]="chave()" />
            <button type="button" class="btn btn-claro btn-sm" aria-label="Copiar api-key"
              (click)="copiar('chave', chave())">Copiar</button>
          </div>
          @if (copiado()) {
            <p class="campo-dica" role="status">{{ copiado() === 'url' ? 'URL copiada.' : 'Api-key copiada.' }}</p>
          }
          <p class="campo-dica">
            No Claude, em Customize › Connectors › Add custom connector, informe esta URL, escolha
            <span class="mono">No sign-in</span> e adicione o cabeçalho <span class="mono">x-api-key</span> com a chave.
          </p>
        </div>
        <div class="dialogo-rodape">
          <button type="button" class="btn btn-primario btn-sm" (click)="fechado.emit()">Fechar</button>
        </div>
      </div>
    </div>
  `,
})
export class ChaveMcpCriadaDialog {
  readonly aviso = AVISO_CHAVE;
  readonly url = environment.mcpUrl;
  readonly chave = input.required<string>();
  readonly fechado = output<void>();
  readonly copiado = signal<'url' | 'chave' | null>(null);

  copiar(campo: 'url' | 'chave', texto: string): void {
    // Sem Clipboard API (HTTP sem TLS, navegador antigo) o texto continua selecionável no campo.
    navigator.clipboard?.writeText(texto).then(
      () => this.copiado.set(campo),
      () => this.copiado.set(null),
    );
  }
}
