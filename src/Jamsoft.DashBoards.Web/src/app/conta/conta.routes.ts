import { Component } from '@angular/core';
import { Routes } from '@angular/router';

/** Placeholder da área até as telas da fase 9/10. */
@Component({ template: '<p>Conta</p>' })
class ContaPlaceholder {}

export default [{ path: '', component: ContaPlaceholder }] satisfies Routes;
