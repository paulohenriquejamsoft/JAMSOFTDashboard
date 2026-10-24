import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { violacoesPoliticaSenha } from '../../core/auth/politica-senha';
import { traduzirProblema } from '../../core/erros/problema';
import { Usuario, UsuariosApi } from './usuarios.api';

/**
 * Redefinir a senha de um usuário do tenant (USR-07): senha temporária pela política da plataforma; a API
 * responde 204, exige troca no próximo login e encerra as sessões dele.
 */
@Component({
  selector: 'app-redefinir-senha',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'fechado.emit()' },
  template: `
    <div class="dialogo-fundo">
      <div class="dialogo" role="dialog" aria-modal="true" aria-labelledby="redefinir-titulo">
        <div class="dialogo-cabecalho">
          <h2 id="redefinir-titulo">Redefinir senha</h2>
        </div>
        <div class="dialogo-corpo">
          @if (concluido()) {
            <p class="alerta alerta-sucesso" role="status">
              Senha de {{ usuario().nome }} redefinida. Ele precisará trocá-la no próximo acesso.
            </p>
          } @else {
            <p>{{ usuario().nome }} ({{ usuario().email }}) terá as sessões encerradas e trocará a senha no próximo acesso.</p>
            <div class="campo">
              <label for="redefinir-senha">Senha temporária <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="redefinir-senha" type="password" autocomplete="new-password" [formControl]="senha"
                [attr.aria-invalid]="!!erro()" aria-describedby="redefinir-senha-erro" />
              @if (erro(); as mensagem) {
                <p id="redefinir-senha-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
          }
        </div>
        <div class="dialogo-rodape">
          @if (concluido()) {
            <button type="button" class="btn btn-primario btn-sm" (click)="fechado.emit()">Fechar</button>
          } @else {
            <button type="button" class="btn btn-contorno btn-sm" (click)="fechado.emit()">Cancelar</button>
            <button type="button" class="btn btn-primario btn-sm" [disabled]="enviando()" (click)="enviar()">
              Redefinir
            </button>
          }
        </div>
      </div>
    </div>
  `,
})
export class RedefinirSenhaDialog {
  private readonly api = inject(UsuariosApi);

  readonly usuario = input.required<Usuario>();
  readonly fechado = output<void>();

  readonly senha = new FormControl('', { nonNullable: true });
  readonly erro = signal<string | null>(null);
  readonly enviando = signal(false);
  readonly concluido = signal(false);

  enviar(): void {
    const violacoes = violacoesPoliticaSenha(this.senha.value);
    if (violacoes.length) {
      this.erro.set(violacoes.join(' '));
      return;
    }
    this.erro.set(null);
    this.enviando.set(true);
    this.api
      .redefinirSenha(this.usuario().idPublico, this.senha.value)
      .pipe(finalize(() => this.enviando.set(false)))
      .subscribe({
        next: () => this.concluido.set(true),
        error: (erro: unknown) => {
          const problema = traduzirProblema(erro);
          this.erro.set(problema.erros['senhaTemporaria']?.join(' ') ?? problema.mensagem);
        },
      });
  }
}
