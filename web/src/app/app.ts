import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'ea-root',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatButtonModule],
  template: `
    <mat-toolbar class="toolbar">
      <a routerLink="/systemer" class="brand">EA-register</a>
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
