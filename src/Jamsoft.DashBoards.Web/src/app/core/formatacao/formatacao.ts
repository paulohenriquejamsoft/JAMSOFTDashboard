import { DATE_PIPE_DEFAULT_OPTIONS, DatePipeConfig, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { DEFAULT_CURRENCY_CODE, EnvironmentProviders, LOCALE_ID, makeEnvironmentProviders } from '@angular/core';

/**
 * Fuso de São Paulo para os pipes de data (AD-018). O `DatePipe` aceita só deslocamento fixo, não nome IANA;
 * São Paulo está em −03:00 o ano todo desde o fim do horário de verão (2019).
 */
export const FUSO_SAO_PAULO = '-0300';

/** Formato pt-BR da aplicação: moeda BRL (R$ 1.234,56), datas dd/MM/yyyy no fuso de São Paulo. */
export function provideFormatacaoPtBr(): EnvironmentProviders {
  registerLocaleData(localePt);
  return makeEnvironmentProviders([
    { provide: LOCALE_ID, useValue: 'pt-BR' },
    { provide: DEFAULT_CURRENCY_CODE, useValue: 'BRL' },
    {
      provide: DATE_PIPE_DEFAULT_OPTIONS,
      useValue: { dateFormat: 'dd/MM/yyyy', timezone: FUSO_SAO_PAULO } satisfies DatePipeConfig,
    },
  ]);
}
