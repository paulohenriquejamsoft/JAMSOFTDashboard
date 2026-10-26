import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { routes } from './app.routes';
import { apiUrlInterceptor } from './core/api-url.interceptor';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { provideFormatacaoPtBr } from './core/formatacao/formatacao';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideFormatacaoPtBr(),
    // Parâmetros de rota (ex.: :id) chegam como input() dos componentes.
    provideRouter(routes, withComponentInputBinding()),
    // apiUrlInterceptor por último: os demais veem as rotas como /api/... relativas.
    provideHttpClient(withInterceptors([authInterceptor, apiUrlInterceptor])),
    // Restaura a sessão pelo cookie de refresh antes da primeira navegação.
    provideAppInitializer(() => inject(AuthService).restaurarSessao()),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
