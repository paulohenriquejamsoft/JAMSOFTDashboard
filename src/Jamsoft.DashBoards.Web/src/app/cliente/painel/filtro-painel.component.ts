import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { RELOGIO } from '../../shared/layout/status-dados.component';
import { Empresa, EmpresasApi } from '../empresas/empresas.api';
import { FiltroPainel } from './painel.service';

/** Limite do período por consulta, igual ao da API (DASH-05). */
export const MESES_MAXIMOS = 24;
export const ERRO_FIM_ANTES_INICIO = 'A data final não pode ser anterior à inicial.';
export const ERRO_PERIODO_MAXIMO = 'O período não pode passar de 24 meses.';
export const ERRO_DATAS_OBRIGATORIAS = 'Informe as datas inicial e final.';

type Atalho = 'este-mes' | 'ultimos-7' | 'ultimos-30' | 'mes-anterior';

/**
 * Filtro do painel (DASH-03..05): datas inicial e final inclusivas, atalhos, empresas (só quando o tenant tem mais
 * de uma) e "Atualizar". Valida antes de emitir: período inválido mostra a mensagem e não consulta. O filtro vive
 * na URL (`?inicio&fim&empresas`); sem parâmetros, abre no mês corrente (fuso de São Paulo). Emite o filtro
 * inicial ao abrir.
 */
@Component({
  selector: 'app-filtro-painel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .atalhos {
      display: flex;
      flex-wrap: wrap;
      gap: var(--espaco-2);
    }
    fieldset {
      border: 0;
      margin: 0;
      padding: 0;
      display: flex;
      flex-wrap: wrap;
      gap: var(--espaco-2) var(--espaco-4);
    }
    legend {
      padding: 0;
      margin-bottom: 5px;
    }
    .data {
      flex: 0 1 160px;
    }
    .mensagem {
      padding: 0 var(--espaco-4) var(--espaco-3);
    }
  `,
  template: `
    <div class="card">
      <form class="barra-ferramentas" (submit)="$event.preventDefault(); atualizar()">
        <div class="campo data">
          <label for="filtro-inicio">Data inicial</label>
          <input id="filtro-inicio" type="date" [value]="inicio()" [attr.aria-invalid]="!!erro()"
            (change)="mudarInicio($any($event.target).value)" />
        </div>
        <div class="campo data">
          <label for="filtro-fim">Data final</label>
          <input id="filtro-fim" type="date" [value]="fim()" [attr.aria-invalid]="!!erro()"
            aria-describedby="filtro-erro" (change)="mudarFim($any($event.target).value)" />
        </div>
        <div class="atalhos" role="group" aria-label="Atalhos de período">
          <button type="button" class="btn btn-claro btn-sm" (click)="aplicarAtalho('este-mes')">Este mês</button>
          <button type="button" class="btn btn-claro btn-sm" (click)="aplicarAtalho('ultimos-7')">Últimos 7 dias</button>
          <button type="button" class="btn btn-claro btn-sm" (click)="aplicarAtalho('ultimos-30')">Últimos 30 dias</button>
          <button type="button" class="btn btn-claro btn-sm" (click)="aplicarAtalho('mes-anterior')">Mês anterior</button>
        </div>
        @if (empresas().length > 1) {
          <fieldset class="campo">
            <legend class="rotulo">Empresas</legend>
            @for (empresa of empresas(); track empresa.idPublico) {
              <label class="marcador">
                <input type="checkbox" [checked]="selecionadas().includes(empresa.idPublico)"
                  (change)="alternarEmpresa(empresa.idPublico, $any($event.target).checked)" />
                {{ empresa.nome }}
              </label>
            }
          </fieldset>
        }
        <button type="submit" class="btn btn-primario">Atualizar</button>
      </form>
      @if (erro(); as mensagem) {
        <div class="mensagem">
          <p id="filtro-erro" class="campo-erro" role="alert">{{ mensagem }}</p>
        </div>
      }
    </div>
  `,
})
export class FiltroPainelComponent implements OnInit {
  private readonly rota = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly agora = inject(RELOGIO);
  private readonly empresasApi = inject(EmpresasApi);

  /** Filtro válido escolhido pelo usuário (ou o inicial, ao abrir). */
  readonly filtrar = output<FiltroPainel>();
  /** Quantas empresas o tenant tem: com uma só, o painel oculta o ranking de empresas (RNK-05). */
  readonly empresasCarregadas = output<number>();

  readonly inicio = signal('');
  readonly fim = signal('');
  readonly selecionadas = signal<string[]>([]);
  readonly empresas = signal<Empresa[]>([]);
  readonly erro = signal<string | null>(null);

  ngOnInit(): void {
    const parametros = this.rota.snapshot.queryParamMap;
    const padrao = periodoDoAtalho('este-mes', this.hoje());
    this.inicio.set(parametros.get('inicio') ?? padrao.inicio);
    this.fim.set(parametros.get('fim') ?? padrao.fim);
    this.selecionadas.set(parametros.getAll('empresas'));
    this.empresasApi.listar().subscribe({
      next: (empresas) => {
        this.empresas.set(empresas);
        this.empresasCarregadas.emit(empresas.length);
      },
      // Sem a lista, o filtro segue com todas as empresas.
      error: () => this.empresas.set([]),
    });
    this.emitir(false);
  }

  mudarInicio(valor: string): void {
    this.inicio.set(valor);
    this.emitir();
  }

  mudarFim(valor: string): void {
    this.fim.set(valor);
    this.emitir();
  }

  aplicarAtalho(atalho: Atalho): void {
    const periodo = periodoDoAtalho(atalho, this.hoje());
    this.inicio.set(periodo.inicio);
    this.fim.set(periodo.fim);
    this.emitir();
  }

  alternarEmpresa(idPublico: string, marcada: boolean): void {
    this.selecionadas.update((atuais) =>
      marcada ? [...atuais, idPublico] : atuais.filter((id) => id !== idPublico),
    );
    this.emitir();
  }

  atualizar(): void {
    this.emitir();
  }

  /** Valida; se válido, emite e (fora da abertura) grava na URL. */
  private emitir(gravarNaUrl = true): void {
    const filtro: FiltroPainel = { inicio: this.inicio(), fim: this.fim(), empresas: this.selecionadas() };
    const erro = validar(filtro.inicio, filtro.fim);
    this.erro.set(erro);
    if (erro) return;
    if (gravarNaUrl) {
      void this.router.navigate([], {
        relativeTo: this.rota,
        queryParams: { inicio: filtro.inicio, fim: filtro.fim, empresas: filtro.empresas.length ? filtro.empresas : null },
      });
    }
    this.filtrar.emit(filtro);
  }

  private hoje(): string {
    return dataEmSaoPaulo(this.agora());
  }
}

/** `yyyy-MM-dd` do instante no fuso de São Paulo (AD-018). */
export function dataEmSaoPaulo(instanteMs: number): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(instanteMs);
}

/** Mensagem do primeiro problema do período, ou `null` se válido (mesma regra da API: DASH-05). */
export function validar(inicio: string, fim: string): string | null {
  if (!inicio || !fim) return ERRO_DATAS_OBRIGATORIAS;
  if (fim < inicio) return ERRO_FIM_ANTES_INICIO;
  if (fim > adicionarDias(adicionarMeses(inicio, MESES_MAXIMOS), -1)) return ERRO_PERIODO_MAXIMO;
  return null;
}

function periodoDoAtalho(atalho: Atalho, hoje: string): { inicio: string; fim: string } {
  const primeiroDoMes = `${hoje.slice(0, 8)}01`;
  switch (atalho) {
    case 'este-mes':
      return { inicio: primeiroDoMes, fim: hoje };
    case 'ultimos-7':
      return { inicio: adicionarDias(hoje, -6), fim: hoje };
    case 'ultimos-30':
      return { inicio: adicionarDias(hoje, -29), fim: hoje };
    case 'mes-anterior':
      return { inicio: adicionarMeses(primeiroDoMes, -1), fim: adicionarDias(primeiroDoMes, -1) };
  }
}

function partes(data: string): [number, number, number] {
  const [ano, mes, dia] = data.split('-').map(Number);
  return [ano, mes - 1, dia];
}

function formatar(utc: Date): string {
  return utc.toISOString().slice(0, 10);
}

function adicionarDias(data: string, dias: number): string {
  const [ano, mes, dia] = partes(data);
  return formatar(new Date(Date.UTC(ano, mes, dia + dias)));
}

/** Como `DateOnly.AddMonths` do .NET: o dia é limitado ao último dia do mês de destino. */
function adicionarMeses(data: string, meses: number): string {
  const [ano, mes, dia] = partes(data);
  const ultimoDia = new Date(Date.UTC(ano, mes + meses + 1, 0)).getUTCDate();
  return formatar(new Date(Date.UTC(ano, mes + meses, Math.min(dia, ultimoDia))));
}
