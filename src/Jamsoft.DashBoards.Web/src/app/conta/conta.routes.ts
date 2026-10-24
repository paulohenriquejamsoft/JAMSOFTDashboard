import { Component } from '@angular/core';
import { Routes } from '@angular/router';

/** Placeholders das telas de conta até a fase 9. */
@Component({ template: '<p>Login</p>' })
class LoginPlaceholder {}

@Component({ template: '<p>Trocar senha</p>' })
class TrocarSenhaPlaceholder {}

export default [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: LoginPlaceholder },
  { path: 'trocar-senha', component: TrocarSenhaPlaceholder },
] satisfies Routes;
