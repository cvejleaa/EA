import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'ea-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule],
  template: `
    <mat-toolbar class="toolbar">
      <a routerLink="/systemer" class="brand">EA-register</a>
      @if (auth.me()) {
        <nav class="nav" aria-label="Hovedmenu">
          @if (auth.me()!.mySystemCount > 0) {
            <a
              mat-button
              routerLink="/systemer"
              [queryParams]="{ mine: 'true' }"
              [class.active]="onMine()"
              data-testid="nav-mine"
            >
              Mine systemer ({{ auth.me()!.mySystemCount }})
            </a>
          }
          <a mat-button routerLink="/systemer" [class.active]="onSystems() && !onMine()" data-testid="nav-systems">
            Systemer
          </a>
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

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );
  /** Systemsiderne (liste og system) — men ikke "Mine systemer", så kun ét menupunkt er markeret (QC). */
  protected readonly onSystems = computed(() => this.router.parseUrl(this.url()).root.children['primary']?.segments[0]?.path === 'systemer');
  protected readonly onMine = computed(() => {
    const tree = this.router.parseUrl(this.url());
    return tree.root.children['primary']?.segments.length === 1 && tree.queryParams['mine'] === 'true';
  });

  protected logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
