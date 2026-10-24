import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'conta' },
  { path: 'conta', loadChildren: () => import('./conta/conta.routes') },
  { path: 'admin', loadChildren: () => import('./admin/admin.routes') },
  { path: 'cliente', loadChildren: () => import('./cliente/cliente.routes') },
  { path: '**', redirectTo: 'conta' },
];
