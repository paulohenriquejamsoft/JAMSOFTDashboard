import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { SeletorTenantComponent } from '../../admin/seletor-tenant/seletor-tenant.component';
import { AVISO_SELECIONE_TENANT } from '../../core/auth/perfil.guard';
import { tenantEfetivo } from '../../core/auth/tenant-efetivo';
import { traduzirProblema } from '../../core/erros/problema';
import { EstadoCarga } from '../../shared/paginado';
import { ConfirmacaoDialog } from '../../shared/ui/confirmacao.dialog';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { RedefinirSenhaDialog } from './redefinir-senha.dialog';
import { Usuario, UsuariosApi } from './usuarios.api';

/**
 * `/cliente/usuarios` (USR-04, USR-09): usuários do tenant (nome, e-mail, perfil, situação) com editar e
 * inativar/reativar e redefinir senha. O SuperAdmin opera no tenant selecionado; sem seleção, a tela mostra o seletor.
 */
@Component({
  selector: 'app-usuarios-lista',
  imports: [RouterLink, EstadoCargaComponent, ConfirmacaoDialog, SeletorTenantComponent, RedefinirSenhaDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Cadastros</span>
          <h1>Usuários</h1>
        </div>
        @if (tenant()) {
          <a class="btn btn-sucesso" routerLink="/cliente/usuarios/novo">Novo usuário</a>
        }
      </div>

      @if (!tenant()) {
        <div class="card">
          <div class="card-corpo">
            <p class="alerta alerta-atencao" role="status">{{ avisoSemTenant }}</p>
            <app-seletor-tenant />
          </div>
        </div>
      } @else {
        <div class="card">
          @if (avisoAcao(); as aviso) {
            <p class="alerta alerta-erro" role="alert" style="margin: 16px 16px 0">{{ aviso }}</p>
          }

          <app-estado-carga
            [estado]="estado()"
            textoVazio="Nenhum usuário cadastrado."
            [mensagemErro]="mensagemErro()"
            (tentarNovamente)="recarregar()"
          >
            <a acaoVazio class="btn btn-sucesso btn-sm" routerLink="/cliente/usuarios/novo">Cadastrar usuário</a>
          </app-estado-carga>

          @if (estado() === 'pronto') {
            <div class="tabela-rolagem">
              <table class="tabela">
                <thead>
                  <tr>
                    <th scope="col">Nome</th>
                    <th scope="col">E-mail</th>
                    <th scope="col">Perfil</th>
                    <th scope="col">Situação</th>
                    <th scope="col"><span class="visualmente-oculto">Ações</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (usuario of usuarios(); track usuario.idPublico) {
                    <tr>
                      <td>{{ usuario.nome }}</td>
                      <td>{{ usuario.email }}</td>
                      <td>{{ usuario.perfil }}</td>
                      <td>
                        <span class="badge" [class.badge-sucesso]="usuario.ativo" [class.badge-neutro]="!usuario.ativo">
                          {{ usuario.ativo ? 'Ativo' : 'Inativo' }}
                        </span>
                      </td>
                      <td class="acoes">
                        <a class="btn btn-claro btn-sm" [routerLink]="['/cliente/usuarios', usuario.idPublico]">Editar</a>
                        <button type="button" class="btn btn-claro btn-sm" (click)="pedirSituacao(usuario)">
                          {{ usuario.ativo ? 'Inativar' : 'Reativar' }}
                        </button>
                        <button type="button" class="btn btn-claro btn-sm" (click)="alvoRedefinir.set(usuario)">
                          Redefinir senha
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      }
    </section>

    @if (alvoSituacao(); as alvo) {
      <app-confirmacao
        [titulo]="alvo.ativo ? 'Inativar usuário' : 'Reativar usuário'"
        [mensagem]="
          alvo.ativo
            ? alvo.nome + ' perderá o acesso imediatamente e as sessões abertas serão encerradas.'
            : alvo.nome + ' voltará a entrar.'
        "
        [textoConfirmar]="alvo.ativo ? 'Inativar' : 'Reativar'"
        [ocupado]="alterandoSituacao()"
        (confirmar)="confirmarSituacao(alvo)"
        (cancelar)="alvoSituacao.set(null)"
      />
    }

    @if (alvoRedefinir(); as alvo) {
      <app-redefinir-senha [usuario]="alvo" (fechado)="alvoRedefinir.set(null)" />
    }
  `,
})
export class UsuariosListaPage {
  private readonly api = inject(UsuariosApi);

  readonly avisoSemTenant = AVISO_SELECIONE_TENANT;
  readonly tenant = tenantEfetivo();
  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly usuarios = signal<Usuario[]>([]);
  readonly alvoSituacao = signal<Usuario | null>(null);
  readonly alterandoSituacao = signal(false);
  readonly alvoRedefinir = signal<Usuario | null>(null);
  readonly avisoAcao = signal<string | null>(null);
  private carga?: Subscription;

  constructor() {
    // Recarrega quando o tenant efetivo muda (SuperAdmin troca a seleção no cabeçalho).
    effect(() => {
      if (this.tenant()) untracked(() => this.recarregar());
    });
  }

  recarregar(): void {
    this.carga?.unsubscribe();
    this.estado.set('carregando');
    this.avisoAcao.set(null);
    this.carga = this.api.listar().subscribe({
      next: (usuarios) => {
        this.usuarios.set(usuarios);
        this.estado.set(usuarios.length === 0 ? 'vazio' : 'pronto');
      },
      error: (erro: unknown) => {
        this.mensagemErro.set(traduzirProblema(erro).mensagem);
        this.estado.set('erro');
      },
    });
  }

  pedirSituacao(usuario: Usuario): void {
    this.avisoAcao.set(null);
    this.alvoSituacao.set(usuario);
  }

  confirmarSituacao(usuario: Usuario): void {
    this.alterandoSituacao.set(true);
    this.api
      .editar(usuario.idPublico, { nome: usuario.nome, perfil: usuario.perfil, ativo: !usuario.ativo, versao: usuario.versao })
      .pipe(finalize(() => this.alterandoSituacao.set(false)))
      .subscribe({
        next: (atualizado) => {
          this.usuarios.update((lista) => lista.map((u) => (u.idPublico === atualizado.idPublico ? atualizado : u)));
          this.alvoSituacao.set(null);
        },
        error: (erro: unknown) => {
          this.alvoSituacao.set(null);
          this.avisoAcao.set(traduzirProblema(erro).mensagem);
        },
      });
  }
}
