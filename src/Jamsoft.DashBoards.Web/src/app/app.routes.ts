import { Routes } from '@angular/router';
import { LEITURA_TENANT, SO_SUPERADMIN, perfilGuard } from './core/auth/perfil.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'conta' },
  { path: 'conta', loadChildren: () => import('./conta/conta.routes') },
  {
    path: 'admin',
    canMatch: [perfilGuard(SO_SUPERADMIN)],
    loadChildren: () => import('./admin/admin.routes'),
  },
  {
    path: 'cliente',
    canMatch: [perfilGuard(LEITURA_TENANT)],
    loadChildren: () => import('./cliente/cliente.routes'),
  },
  { path: '**', redirectTo: 'conta' },
];
