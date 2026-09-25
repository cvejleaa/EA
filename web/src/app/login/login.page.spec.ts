import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { DevUserResponse } from '../api/types';
import { provideDanishLocale } from '../core/locale';
import { settle, text } from '../testing/fixtures';
import { LoginPage } from './login.page';

describe('LoginPage (udviklingslogin)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
  });

  it('viser app-rollen efter navnet — og intet efter en bruger uden app-rolle', async () => {
    const f = TestBed.createComponent(LoginPage);
    const http = TestBed.inject(HttpTestingController);
    await settle(f);
    http.expectOne('/api/auth/mode').flush({ mode: 'Dev' });
    await settle(f);
    // FIKTIVE brugere. En forvalter har ingen app-rolle, men kan redigere sine systemer: "· læser" ville være forkert.
    const users: DevUserResponse[] = [
      { id: 'eva', name: 'Eva Arkitekt (fiktiv)', roles: ['EA.Admin'] },
      { id: 'frida', name: 'Frida Forvalter (fiktiv)', roles: [] },
    ];
    http.expectOne('/api/dev/users').flush(users);
    await settle(f);

    const buttons = Array.from((f.nativeElement as HTMLElement).querySelectorAll('button')).map((b) => text(b));
    expect(buttons).toEqual(['Eva Arkitekt (fiktiv) · EA.Admin', 'Frida Forvalter (fiktiv)']);
    http.verify();
  });
});
