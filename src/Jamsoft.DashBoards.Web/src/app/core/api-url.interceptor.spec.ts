import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { apiUrlInterceptor } from './api-url.interceptor';

describe('apiUrlInterceptor', () => {
  const apiUrlOriginal = environment.apiUrl;
  let http: HttpClient;
  let api: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiUrlInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    api = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    environment.apiUrl = apiUrlOriginal;
    api.verify();
  });

  it('mantém /api relativo quando apiUrl é vazio (proxy do ng serve)', () => {
    environment.apiUrl = '';
    http.get('/api/auth/eu').subscribe();
    api.expectOne('/api/auth/eu').flush({});
  });

  it('prefixa /api com apiUrl', () => {
    environment.apiUrl = 'https://sistemas.ilojajamsoft.com.br/JamsoftDashboardApi';
    http.get('/api/painel/kpis').subscribe();
    api
      .expectOne('https://sistemas.ilojajamsoft.com.br/JamsoftDashboardApi/api/painel/kpis')
      .flush({});
  });

  it('não altera URLs fora da API', () => {
    environment.apiUrl = 'https://sistemas.ilojajamsoft.com.br/JamsoftDashboardApi';
    http.get('/manifest.webmanifest').subscribe();
    api.expectOne('/manifest.webmanifest').flush({});
  });
});
