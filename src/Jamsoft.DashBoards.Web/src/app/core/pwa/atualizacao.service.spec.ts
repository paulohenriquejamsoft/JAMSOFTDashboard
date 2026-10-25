import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SwUpdate, VersionEvent, provideServiceWorker } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { App } from '../../app';
import { RECARREGAR_PAGINA } from './atualizacao.service';

const VERSAO_PRONTA: VersionEvent = {
  type: 'VERSION_READY',
  currentVersion: { hash: 'a1' },
  latestVersion: { hash: 'b2' },
};

describe('Aviso "Nova versão disponível" (PWA-04)', () => {
  let versoes: Subject<VersionEvent>;
  let activateUpdate: ReturnType<typeof vi.fn>;
  let recarregar: ReturnType<typeof vi.fn>;

  async function abrir(providers: unknown[]): Promise<HTMLElement> {
    recarregar = vi.fn();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: RECARREGAR_PAGINA, useValue: recarregar }, ...(providers as [])],
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function swFalso(): unknown[] {
    versoes = new Subject<VersionEvent>();
    activateUpdate = vi.fn().mockResolvedValue(true);
    return [{ provide: SwUpdate, useValue: { isEnabled: true, versionUpdates: versoes, activateUpdate } }];
  }

  const faixa = (tela: HTMLElement) => tela.querySelector<HTMLElement>('.faixa-atualizacao');

  it('VERSION_READY mostra a faixa com "Recarregar"', async () => {
    const tela = await abrir(swFalso());
    expect(faixa(tela)).toBeNull();

    versoes.next(VERSAO_PRONTA);
    TestBed.tick();

    expect(faixa(tela)?.textContent).toContain('Nova versão disponível');
    expect(faixa(tela)?.querySelector('button')?.textContent?.trim()).toBe('Recarregar');
  });

  it('"Recarregar" ativa a nova versão e recarrega a página', async () => {
    const tela = await abrir(swFalso());
    versoes.next(VERSAO_PRONTA);
    TestBed.tick();

    faixa(tela)!.querySelector('button')!.click();
    await vi.waitFor(() => expect(recarregar).toHaveBeenCalledTimes(1));

    expect(activateUpdate).toHaveBeenCalledTimes(1);
    expect(activateUpdate.mock.invocationCallOrder[0]).toBeLessThan(recarregar.mock.invocationCallOrder[0]);
  });

  it('outros eventos de versão não mostram a faixa', async () => {
    const tela = await abrir(swFalso());

    versoes.next({ type: 'VERSION_DETECTED', version: { hash: 'b2' } });
    versoes.next({ type: 'NO_NEW_VERSION_DETECTED', version: { hash: 'a1' } });
    versoes.next({ type: 'VERSION_INSTALLATION_FAILED', version: { hash: 'b2' }, error: 'falhou' });
    TestBed.tick();

    expect(faixa(tela)).toBeNull();
  });

  it('com o service worker desabilitado a aplicação abre sem faixa e sem erro', async () => {
    const tela = await abrir([provideServiceWorker('ngsw-worker.js', { enabled: false })]);
    TestBed.tick();

    expect(tela.querySelector('router-outlet')).not.toBeNull();
    expect(faixa(tela)).toBeNull();
  });
});
