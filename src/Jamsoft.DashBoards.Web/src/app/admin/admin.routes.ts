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
      {
        path: 'tenants/novo',
        loadComponent: () => import('./tenants/tenant-form.page').then((m) => m.TenantFormPage),
      },
      {
        path: 'tenants/:id',
        loadComponent: () => import('./tenants/tenant-form.page').then((m) => m.TenantFormPage),
      },
      {
        path: 'auditoria',
        loadComponent: () => import('./auditoria/auditoria.page').then((m) => m.AuditoriaPage),
      },
    ],
  },
] satisfies Routes;
