import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth.service';
import { provideDanishLocale } from './core/locale';
import { me, settle } from './testing/fixtures';

@Component({ template: '' })
class Blank {}

describe('App (hovedmenu)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'systemer', component: Blank },
          { path: 'systemer/:id', component: Blank },
          { path: 'kapabiliteter', component: Blank },
        ]),
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

  it('"Mine systemer (N)" står først, når brugeren har en rolle på systemer', async () => {
    TestBed.inject(AuthService).me.set(me(false, { mySystemCount: 3 }));
    const f = TestBed.createComponent(App);
    await settle(f);

    const links = Array.from((f.nativeElement as HTMLElement).querySelectorAll('nav a')).map(
      (a) => [a.textContent?.trim(), a.getAttribute('href')],
    );
    expect(links).toEqual([
      ['Mine systemer (3)', '/systemer?mine=true'],
      ['Systemer', '/systemer'],
      ['Kapabiliteter', '/kapabiliteter'],
    ]);
  });

  it('kun ét af "Mine systemer" og "Systemer" er markeret ad gangen', async () => {
    TestBed.inject(AuthService).me.set(me(false, { mySystemCount: 3 }));
    const f = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    const active = () =>
      Array.from((f.nativeElement as HTMLElement).querySelectorAll('nav a.active')).map((a) => a.getAttribute('data-testid'));

    await router.navigateByUrl('/systemer?mine=true');
    await settle(f);
    expect(active()).toEqual(['nav-mine']);

    await router.navigateByUrl('/systemer?status=IDrift');
    await settle(f);
    expect(active()).toEqual(['nav-systems']);

    await router.navigateByUrl('/systemer/sys-1');
    await settle(f);
    expect(active()).toEqual(['nav-systems']);

    await router.navigateByUrl('/kapabiliteter');
    await settle(f);
    expect(active()).toEqual(['nav-capabilities']);
  });

  it('uden roller på systemer er der intet "Mine systemer"', async () => {
    TestBed.inject(AuthService).me.set(me(false, { mySystemCount: 0 }));
    const f = TestBed.createComponent(App);
    await settle(f);

    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="nav-mine"]')).toBeNull();
  });

  it('uden login er der ingen menu', async () => {
    const f = TestBed.createComponent(App);
    await settle(f);

    expect((f.nativeElement as HTMLElement).querySelector('nav')).toBeNull();
  });
});
