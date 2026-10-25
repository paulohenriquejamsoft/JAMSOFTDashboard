import { Routes } from '@angular/router';
import { QUALQUER_PERFIL, perfilGuard } from '../core/auth/perfil.guard';
import { ShellComponent } from '../shared/layout/shell.component';

export default [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', loadComponent: () => import('./login/login.page').then((m) => m.LoginPage) },
  {
    path: 'trocar-senha',
    loadComponent: () => import('./trocar-senha/trocar-senha.page').then((m) => m.TrocarSenhaPage),
  },
  // "Meu perfil" fica dentro do layout logado, para qualquer perfil (UI-01).
  {
    path: 'perfil',
    canMatch: [perfilGuard(QUALQUER_PERFIL)],
    component: ShellComponent,
    children: [{ path: '', loadComponent: () => import('./perfil/perfil.page').then((m) => m.PerfilPage) }],
  },
] satisfies Routes;
