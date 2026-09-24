import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login.page').then((m) => m.LoginPage), title: 'Log ind' },
  {
    path: 'systemer',
    canActivate: [authGuard],
    children: [
      { path: '', loadComponent: () => import('./systems/system-list.page').then((m) => m.SystemListPage), title: 'Systemer' },
      { path: 'ny', loadComponent: () => import('./systems/system-form.page').then((m) => m.SystemFormPage), title: 'Nyt system' },
      { path: ':id', loadComponent: () => import('./systems/system-detail.page').then((m) => m.SystemDetailPage), title: 'System' },
      {
        path: ':id/rediger',
        loadComponent: () => import('./systems/system-form.page').then((m) => m.SystemFormPage),
        title: 'Rediger system',
      },
    ],
  },
  { path: '', pathMatch: 'full', redirectTo: 'systemer' },
  { path: '**', redirectTo: 'systemer' },
];
