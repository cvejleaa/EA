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
      {
        path: ':id/integrationer/ny',
        loadComponent: () => import('./integrations/integration-form.page').then((m) => m.IntegrationFormPage),
        title: 'Ny integration',
      },
      {
        path: ':id/integrationer/:integrationId/rediger',
        loadComponent: () => import('./integrations/integration-form.page').then((m) => m.IntegrationFormPage),
        title: 'Rediger integration',
      },
    ],
  },
  {
    path: 'kapabiliteter',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./capabilities/capability-map.page').then((m) => m.CapabilityMapPage),
        title: 'Kapabiliteter',
      },
      {
        path: 'import',
        loadComponent: () => import('./capabilities/capability-import.page').then((m) => m.CapabilityImportPage),
        title: 'Importér kapabilitetskortet',
      },
      {
        path: 'koblinger/import',
        loadComponent: () => import('./capabilities/coupling-import.page').then((m) => m.CouplingImportPage),
        title: 'Importér koblinger',
      },
    ],
  },
  { path: '', pathMatch: 'full', redirectTo: 'systemer' },
  { path: '**', redirectTo: 'systemer' },
];
