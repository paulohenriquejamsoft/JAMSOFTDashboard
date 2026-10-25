import { CurrencyPipe, DATE_PIPE_DEFAULT_OPTIONS, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideFormatacaoPtBr } from './formatacao';

@Component({
  imports: [CurrencyPipe, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span id="moeda">{{ 1234.56 | currency }}</span><span id="data">{{ instante | date }}</span>`,
})
class FormatadoComponent {
  /** 24/10/2026 22:30 em São Paulo (−03:00); 25/10 em UTC. */
  readonly instante = '2026-10-25T01:30:00Z';
}

describe('formatação pt-BR', () => {
  it('pipes formatam R$ 1.234,56 e 24/10/2026 no fuso de São Paulo', async () => {
    TestBed.configureTestingModule({ imports: [FormatadoComponent], providers: [provideFormatacaoPtBr()] });
    const fixture = TestBed.createComponent(FormatadoComponent);
    await fixture.whenStable();
    const texto = (id: string) => (fixture.nativeElement as HTMLElement).querySelector(`#${id}`)!.textContent!;

    expect(texto('moeda').replaceAll(String.fromCharCode(0xa0), ' ')).toBe('R$ 1.234,56');
    expect(texto('data')).toBe('24/10/2026');
    expect(TestBed.inject(DATE_PIPE_DEFAULT_OPTIONS).timezone).toBe('-0300');
  });
});
