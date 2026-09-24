import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { AuthModeResponse, DevUserResponse } from '../api/types';
import { AuthService } from '../core/auth.service';
import { toProblem } from '../core/problem';

@Component({
  selector: 'ea-login-page',
  imports: [MatButtonModule],
  template: `
    <section class="page">
      <h1>Log ind</h1>
      @if (mode() === 'Dev') {
        <p class="muted">Udviklingslogin — vælg en fiktiv bruger.</p>
        <div class="row">
          @for (user of users(); track user.id) {
            <button mat-stroked-button type="button" (click)="login(user.id)">
              {{ user.name }}{{ user.roles.length ? ' · ' + user.roles.join(', ') : ' · læser' }}
            </button>
          }
        </div>
      } @else if (mode()) {
        <p>Log ind med DTU-konto er ikke sat op endnu.</p>
      }
      @if (error()) {
        <p class="error-text" role="alert">{{ error() }}</p>
      }
    </section>
  `,
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly mode = signal<string | null>(null);
  protected readonly users = signal<DevUserResponse[]>([]);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      const { mode } = await firstValueFrom(this.http.get<AuthModeResponse>('/api/auth/mode'));
      this.mode.set(mode);
      if (mode === 'Dev') {
        this.users.set(await this.auth.devUsers());
      }
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  protected async login(userId: string): Promise<void> {
    try {
      await this.auth.loginDev(userId);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      await this.router.navigateByUrl(returnUrl?.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/systemer');
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }
}
