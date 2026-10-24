import { Routes } from '@angular/router';
import { ShellComponent } from '../shared/layout/shell.component';

/** Área do SuperAdmin, dentro do layout logado. */
export default [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', loadComponent: () => import('./admin-inicio.page').then((m) => m.AdminInicioPage) },
      {
        path: 'tenants',
        loadComponent: () => import('./tenants/tenants-lista.page').then((m) => m.TenantsListaPage),
      },
    ],
  },
] satisfies Routes;
