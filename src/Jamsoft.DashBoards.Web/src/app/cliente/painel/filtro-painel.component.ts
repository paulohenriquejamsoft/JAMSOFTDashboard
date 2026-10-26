import { ChangeDetectionStrategy, Component, ElementRef, OnInit, computed, inject, output, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { RELOGIO } from '../../shared/layout/status-dados.component';
import { Empresa, EmpresasApi } from '../empresas/empresas.api';
import { FiltroPainel, PainelService, TIPOS_DOCUMENTO, TipoDocumento, Vendedor } from './painel.service';

/** Limite do período por consulta, igual ao da API (DASH-05). */
export const MESES_MAXIMOS = 24;
export const ERRO_FIM_ANTES_INICIO = 'A data final não pode ser anterior à inicial.';
export const ERRO_PERIODO_MAXIMO = 'O período não pode passar de 24 meses.';
export const ERRO_DATAS_OBRIGATORIAS = 'Informe as datas inicial e final.';

type Atalho = 'este-mes' | 'ultimos-7' | 'ultimos-30' | 'mes-anterior';

/**
 * Filtro do painel (DASH-03..05, DASH-10): datas inicial e final inclusivas, atalhos, empresas (só quando o tenant
 * tem mais de uma), tipos de documento, vendedor (só quando há vendedores) e "Atualizar". Valida antes de emitir:
 * período inválido mostra a mensagem e não consulta. O filtro vive na URL (`?inicio&fim&empresas&tipos&vendedor`);
 * sem parâmetros, abre no mês corrente (fuso de São Paulo). Emite o filtro inicial ao abrir.
 */
@Component({
  selector: 'app-filtro-painel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'fecharEmpresasFora($event)',
    '(document:keydown.escape)': 'empresasAbertas.set(false)',
  },
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
    .vendedor {
      flex: 0 1 220px;
    }
    /* O .card global corta o conteúdo (overflow: hidden); aqui o painel de empresas precisa vazar. */
    .card {
      overflow: visible;
    }
    .empresas {
      flex: 0 1 240px;
      position: relative;
    }
    .seletor {
      height: 38px;
      box-sizing: border-box;
      padding: 0 32px 0 12px;
      border-radius: var(--raio-campo);
      border: 1px solid #e8e8e8;
      background: #f8f8f8;
      font: inherit;
      font-size: var(--font-size-base);
      color: var(--text-primary);
      text-align: left;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      cursor: pointer;
      position: relative;
    }
    .seletor::after {
      content: '';
      position: absolute;
      right: 12px;
      top: 50%;
      width: 7px;
      height: 7px;
      border-right: 2px solid currentColor;
      border-bottom: 2px solid currentColor;
      transform: translateY(-70%) rotate(45deg);
    }
    .seletor:focus-visible,
    .seletor[aria-expanded='true'] {
      outline: none;
      border-color: var(--primary);
      background: var(--surface-bg);
      box-shadow: var(--sombra-foco);
    }
    .painel-empresas {
      position: absolute;
      top: calc(100% + 4px);
      left: 0;
      z-index: 20;
      min-width: 100%;
      max-height: 320px;
      overflow-y: auto;
      box-sizing: border-box;
      padding: var(--espaco-2);
      background: var(--surface-bg, #fff);
      border: 1px solid var(--borda-suave);
      border-radius: var(--raio-campo);
      box-shadow: var(--sombra-card);
      display: flex;
      flex-direction: column;
      flex-wrap: nowrap;
      gap: 0;
    }
    .painel-empresas[hidden] {
      display: none;
    }
    .painel-empresas .marcador {
      padding: 6px 8px;
      border-radius: var(--raio-campo);
      text-transform: none;
      letter-spacing: normal;
      font-weight: normal;
      color: var(--text-primary);
      white-space: nowrap;
      cursor: pointer;
    }
    .painel-empresas .marcador:hover {
      background: #f5f5f5;
    }
    .acoes-empresas {
      display: flex;
      justify-content: space-between;
      padding: 0 8px 6px;
      margin-bottom: 4px;
      border-bottom: 1px solid var(--borda-suave);
    }
    .acoes-empresas button {
      border: 0;
      background: none;
      padding: 0;
      font: inherit;
      font-size: var(--dica-size);
      color: var(--primary-texto);
      cursor: pointer;
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
          <div class="campo empresas">
            <span class="rotulo" id="filtro-empresas-rotulo">Empresas</span>
            <button type="button" class="seletor" aria-haspopup="true" aria-labelledby="filtro-empresas-rotulo filtro-empresas"
              id="filtro-empresas" [attr.aria-expanded]="empresasAbertas()" (click)="empresasAbertas.set(!empresasAbertas())">
              {{ resumoEmpresas() }}
            </button>
            <fieldset class="painel-empresas" [hidden]="!empresasAbertas()" aria-labelledby="filtro-empresas-rotulo">
              <div class="acoes-empresas">
                <button type="button" (click)="marcarTodasEmpresas()">Marcar todas</button>
                <button type="button" (click)="limparEmpresas()">Limpar</button>
              </div>
              @for (empresa of empresas(); track empresa.idPublico) {
                <label class="marcador">
                  <input type="checkbox" [checked]="selecionadas().includes(empresa.idPublico)"
                    (change)="alternarEmpresa(empresa.idPublico, $any($event.target).checked)" />
                  {{ empresa.nome }}
                </label>
              }
            </fieldset>
          </div>
        }
        <fieldset class="campo tipos">
          <legend class="rotulo">Tipo de documento</legend>
          @for (tipo of tiposDocumento; track tipo) {
            <label class="marcador">
              <input type="checkbox" [checked]="tipos().includes(tipo)"
                (change)="alternarTipo(tipo, $any($event.target).checked)" />
              {{ tipo }}
            </label>
          }
        </fieldset>
        @if (vendedores().length) {
          <div class="campo vendedor">
            <label for="filtro-vendedor">Vendedor</label>
            <select id="filtro-vendedor" (change)="mudarVendedor($any($event.target).value)">
              <option value="" [selected]="vendedor() === null">Todos</option>
              @for (v of vendedores(); track v.codigo) {
                <option [value]="v.codigo" [selected]="vendedor() === v.codigo">{{ v.nome }}</option>
              }
            </select>
          </div>
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
  private readonly painel = inject(PainelService);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Filtro válido escolhido pelo usuário (ou o inicial, ao abrir). */
  readonly filtrar = output<FiltroPainel>();
  /** Quantas empresas o tenant tem: com uma só, o painel oculta o ranking de empresas (RNK-05). */
  readonly empresasCarregadas = output<number>();

  readonly inicio = signal('');
  readonly fim = signal('');
  readonly selecionadas = signal<string[]>([]);
  readonly empresas = signal<Empresa[]>([]);
  readonly tiposDocumento = TIPOS_DOCUMENTO;
  readonly tipos = signal<TipoDocumento[]>([]);
  /** Código do vendedor escolhido; `null` = todos. */
  readonly vendedor = signal<number | null>(null);
  readonly vendedores = signal<Vendedor[]>([]);
  readonly erro = signal<string | null>(null);
  readonly empresasAbertas = signal(false);
  /** Texto do seletor fechado: nenhuma marcada = todas. */
  readonly resumoEmpresas = computed(() => {
    const marcadas = this.empresas().filter((e) => this.selecionadas().includes(e.idPublico));
    if (!marcadas.length || marcadas.length === this.empresas().length) return 'Todas';
    if (marcadas.length === 1) return marcadas[0].nome;
    return `${marcadas.length} empresas`;
  });

  ngOnInit(): void {
    const parametros = this.rota.snapshot.queryParamMap;
    const padrao = periodoDoAtalho('este-mes', this.hoje());
    this.inicio.set(parametros.get('inicio') ?? padrao.inicio);
    this.fim.set(parametros.get('fim') ?? padrao.fim);
    this.selecionadas.set(parametros.getAll('empresas'));
    this.tipos.set(TIPOS_DOCUMENTO.filter((tipo) => parametros.getAll('tipos').includes(tipo)));
    const vendedor = Number(parametros.get('vendedor'));
    this.vendedor.set(Number.isInteger(vendedor) && vendedor > 0 ? vendedor : null);
    this.empresasApi.listar().subscribe({
      next: (empresas) => {
        this.empresas.set(empresas);
        this.empresasCarregadas.emit(empresas.length);
      },
      // Sem a lista, o filtro segue com todas as empresas.
      error: () => this.empresas.set([]),
    });
    this.painel.vendedores().subscribe({
      next: (vendedores) => this.vendedores.set(vendedores),
      // Sem a lista, o seletor some; um vendedor vindo da URL continua no filtro.
      error: () => this.vendedores.set([]),
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

  marcarTodasEmpresas(): void {
    this.selecionadas.set(this.empresas().map((e) => e.idPublico));
    this.emitir();
  }

  limparEmpresas(): void {
    this.selecionadas.set([]);
    this.emitir();
  }

  fecharEmpresasFora(evento: Event): void {
    if (!this.empresasAbertas()) return;
    const painel = this.elemento.nativeElement.querySelector('.empresas');
    if (painel && !painel.contains(evento.target as Node)) this.empresasAbertas.set(false);
  }

  alternarTipo(tipo: TipoDocumento, marcado: boolean): void {
    // Mantém a ordem do domínio, para a URL não depender da ordem dos cliques.
    this.tipos.update((atuais) => TIPOS_DOCUMENTO.filter((t) => (t === tipo ? marcado : atuais.includes(t))));
    this.emitir();
  }

  mudarVendedor(valor: string): void {
    this.vendedor.set(valor ? Number(valor) : null);
    this.emitir();
  }

  atualizar(): void {
    this.emitir();
  }

  /** Valida; se válido, emite e (fora da abertura) grava na URL. */
  private emitir(gravarNaUrl = true): void {
    const vendedor = this.vendedor();
    const filtro: FiltroPainel = {
      inicio: this.inicio(),
      fim: this.fim(),
      empresas: this.selecionadas(),
      tipos: this.tipos(),
      vendedores: vendedor === null ? [] : [vendedor],
    };
    const erro = validar(filtro.inicio, filtro.fim);
    this.erro.set(erro);
    if (erro) return;
    if (gravarNaUrl) {
      void this.router.navigate([], {
        relativeTo: this.rota,
        queryParams: {
          inicio: filtro.inicio,
          fim: filtro.fim,
          empresas: filtro.empresas.length ? filtro.empresas : null,
          tipos: filtro.tipos.length ? filtro.tipos : null,
          vendedor,
        },
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
