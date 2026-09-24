import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'ea-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule],
  template: `
    <mat-toolbar class="toolbar">
      <a routerLink="/systemer" class="brand">EA-register</a>
      @if (auth.me()) {
        <nav class="nav" aria-label="Hovedmenu">
          <a mat-button routerLink="/systemer" routerLinkActive="active" data-testid="nav-systems">Systemer</a>
          <a mat-button routerLink="/kapabiliteter" routerLinkActive="active" data-testid="nav-capabilities">
            Kapabiliteter
          </a>
        </nav>
      }
      <span class="spacer"></span>
      @if (auth.me(); as me) {
        <span class="user">{{ me.name }}</span>
        <button mat-button type="button" (click)="logout()">Log ud</button>
      }
    </mat-toolbar>
    <div class="prototype-banner" role="note">Prototype — alle data er fiktive</div>
    <main>
      <router-outlet />
    </main>
  `,
  styles: `
    .toolbar { gap: 12px; }
    .brand { color: inherit; text-decoration: none; font-weight: 600; }
    .spacer { flex: 1; }
    .nav { display: flex; gap: 4px; }
    .nav .active { background: var(--mat-sys-secondary-container); }
    .user { font: var(--mat-sys-body-medium); }
    .prototype-banner {
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
      padding: 4px 16px;
      font: var(--mat-sys-label-large);
    }
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
