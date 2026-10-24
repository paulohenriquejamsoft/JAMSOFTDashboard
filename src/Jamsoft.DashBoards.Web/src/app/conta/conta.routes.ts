import { Routes } from '@angular/router';

export default [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', loadComponent: () => import('./login/login.page').then((m) => m.LoginPage) },
  {
    path: 'trocar-senha',
    loadComponent: () => import('./trocar-senha/trocar-senha.page').then((m) => m.TrocarSenhaPage),
  },
] satisfies Routes;
