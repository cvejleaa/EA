import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { DevTokenResponse, DevUserResponse, MeResponse } from '../api/types';

const TOKEN_KEY = 'ea.accessToken';

/**
 * Login og den aktuelle bruger. I prototypen udstedes tokens af API'ets dev-login; i produktion
 * erstatter MSAL (Entra ID) kun denne service — resten af klienten bruger blot getToken()/me().
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly me = signal<MeResponse | null>(null);

  getToken(): string | null {
    try {
      return sessionStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  }

  devUsers(): Promise<DevUserResponse[]> {
    return firstValueFrom(this.http.get<DevUserResponse[]>('/api/dev/users'));
  }

  async loginDev(userId: string): Promise<void> {
    const response = await firstValueFrom(this.http.post<DevTokenResponse>('/api/dev/token', { userId }));
    try {
      sessionStorage.setItem(TOKEN_KEY, response.accessToken);
    } catch {
      // Uden sessionStorage virker login kun, til siden genindlæses.
    }
    await this.loadMe();
  }

  async loadMe(): Promise<MeResponse> {
    const me = await firstValueFrom(this.http.get<MeResponse>('/api/me'));
    this.me.set(me);
    return me;
  }

  logout(): void {
    try {
      sessionStorage.removeItem(TOKEN_KEY);
    } catch {
      // Intet at rydde.
    }
    this.me.set(null);
  }
}
