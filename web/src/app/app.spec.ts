import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth.service';
import { provideDanishLocale } from './core/locale';
import { me, settle } from './testing/fixtures';

describe('App (hovedmenu)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideDanishLocale(),
      ],
    });
  });

  it('en logget-ind bruger har "Systemer" og "Kapabiliteter" i menuen', async () => {
    TestBed.inject(AuthService).me.set(me(false));
    const f = TestBed.createComponent(App);
    await settle(f);

    const links = Array.from((f.nativeElement as HTMLElement).querySelectorAll('nav a')).map(
      (a) => [a.textContent?.trim(), a.getAttribute('href')],
    );
    expect(links).toEqual([
      ['Systemer', '/systemer'],
      ['Kapabiliteter', '/kapabiliteter'],
    ]);
  });

  it('uden login er der ingen menu', async () => {
    const f = TestBed.createComponent(App);
    await settle(f);

    expect((f.nativeElement as HTMLElement).querySelector('nav')).toBeNull();
  });
});
