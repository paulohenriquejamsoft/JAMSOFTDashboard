import { Component } from '@angular/core';
import { Routes } from '@angular/router';

/** Placeholder da troca de senha até a T49. */
@Component({ template: '<p>Trocar senha</p>' })
class TrocarSenhaPlaceholder {}

export default [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', loadComponent: () => import('./login/login.page').then((m) => m.LoginPage) },
  { path: 'trocar-senha', component: TrocarSenhaPlaceholder },
] satisfies Routes;
