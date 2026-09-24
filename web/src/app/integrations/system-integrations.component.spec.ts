import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { SystemIntegrationsResponse } from '../api/types';
import { provideDanishLocale } from '../core/locale';
import { integration, integrationItem, integrations, settle, systemLink, text } from '../testing/fixtures';
import { SystemIntegrationsComponent } from './system-integrations.component';

describe('SystemIntegrationsComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(response: SystemIntegrationsResponse): Promise<ComponentFixture<SystemIntegrationsComponent>> {
    const fixture = TestBed.createComponent(SystemIntegrationsComponent);
    fixture.componentRef.setInput('systemId', 'sys-1');
    fixture.detectChanges();
    http.expectOne('/api/systems/sys-1/integrations').flush(response);
    await settle(fixture);
    return fixture;
  }

  const el = (f: ComponentFixture<unknown>) => f.nativeElement as HTMLElement;
  const q = (f: ComponentFixture<unknown>, selector: string) => el(f).querySelector(selector);
  const rows = (f: ComponentFixture<unknown>) =>
    Array.from(el(f).querySelectorAll('tbody tr')).map((r) => Array.from(r.querySelectorAll('td')).map((c) => text(c)));

  it('viser relation med retning, det andet system og markeringer', async () => {
    const f = await render(
      integrations([
        integrationItem({
          relation: 'Ud',
          // Kun "lokal løsning" (i drift) her og kun livscyklus på HR nedenfor: de to markeringer er uafhængige.
          counterpart: systemLink('Lønudtræk', { type: 'LokalLoesning', lifecycleStatus: 'IDrift' }),
          localModule: { id: 'm', name: 'HR' },
          integration: integration({ type: 'Udtraek', name: 'Månedligt', description: 'Til lønkontoret' }),
        }),
        integrationItem({
          relation: 'Ind',
          counterpart: systemLink('HR', { parent: { id: 'p', name: 'Nordlys' }, lifecycleStatus: 'Udfases' }),
          integration: integration({ id: 'int-2', type: null, dataObjects: [{ id: 'd1', name: 'Medarbejder' }, { id: 'd2', name: 'Løn' }] }),
        }),
        integrationItem({
          relation: 'Via',
          counterpart: null,
          integration: integration({ id: 'int-3', from: systemLink('A'), to: systemLink('B'), via: systemLink('Platformen') }),
        }),
      ]),
    );

    const [ud, ind, via] = rows(f);
    expect(ud.slice(0, 3)).toEqual(['Sender data til', 'Lønudtræk Lokal løsning/udtræk (modul: HR)', 'Udtræk']);
    expect(ud[5]).toBe('Månedligt Til lønkontoret');
    expect(ind.slice(0, 5)).toEqual(['Modtager data fra', 'Nordlys › HR Udfases', 'Ikke angivet', '', 'Medarbejder, Løn']);
    expect(via.slice(0, 4)).toEqual(['Går via platformen', 'A → B', 'API', 'Platformen']);
  });

  it('tællelinjen skriver enheden ved hvert tal og viser kun det relevante', async () => {
    const f = await render(
      integrations([integrationItem()], {
        summary: { receivers: 4, suppliers: 1, viaPlatform: 0, localSolutions: 0, directDb: 0 },
      }),
    );

    expect(text(q(f, '[data-testid="summary"]'))).toBe('Sender data til 4 systemer · modtager data fra 1 system');
    expect(q(f, '[data-testid="direct-db"]')).toBeNull();
  });

  it('tællelinjen nævner lokale løsninger/udtræk, platformtrafik og direkte databaseadgang, når de findes', async () => {
    const f = await render(
      integrations([integrationItem()], {
        summary: { receivers: 1, suppliers: 0, viaPlatform: 2, localSolutions: 1, directDb: 3 },
      }),
    );

    expect(text(q(f, '[data-testid="summary"]'))).toBe(
      'Sender data til 1 system · modtager data fra 0 systemer · 1 af modparterne er af typen Lokal løsning/udtræk · 2 integrationer går via platformen',
    );
    expect(text(q(f, '[data-testid="direct-db"]'))).toBe('3 integrationer med direkte databaseadgang (brud på API First)');
  });

  it('ental i advarslen om direkte databaseadgang', async () => {
    const f = await render(
      integrations([integrationItem()], { summary: { receivers: 1, suppliers: 0, viaPlatform: 0, localSolutions: 2, directDb: 1 } }),
    );

    expect(text(q(f, '[data-testid="direct-db"]'))).toBe('1 integration med direkte databaseadgang (brud på API First)');
    expect(text(q(f, '[data-testid="summary"]'))).toContain('2 af modparterne er af typen Lokal løsning/udtræk');
    // Beslutning F: "løsninger" (flertal) må ikke stå på skærmen.
    expect(text(el(f))).not.toContain('løsninger');
  });

  it('tom tilstand lover ikke, at intet hænger på systemet', async () => {
    const f = await render(integrations([]));

    expect(text(q(f, '[data-testid="empty"]'))).toBe(
      'Ingen registrerede integrationer — registret er ikke komplet her endnu.',
    );
    expect(q(f, '[data-testid="summary"]')).toBeNull();
    expect(q(f, 'table')).toBeNull();
  });

  it('"Tilføj integration" og "Rediger" vises kun, når serveren giver lov', async () => {
    const reader = await render(
      integrations([integrationItem({ integration: integration({ permissions: { canEdit: false } }) })], { canAdd: false }),
    );
    expect(q(reader, '[data-testid="add"]')).toBeNull();
    expect(text(el(reader))).not.toContain('Rediger');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
    http = TestBed.inject(HttpTestingController);
    const admin = await render(integrations([integrationItem()], { canAdd: true }));
    expect(q(admin, '[data-testid="add"]')?.getAttribute('href')).toBe('/systemer/sys-1/integrationer/ny');
    expect(el(admin).querySelector('tbody a[href$="/rediger"]')?.getAttribute('href')).toBe(
      '/systemer/sys-1/integrationer/int-1/rediger',
    );
  });

  it('en fejl vises i sektionen i stedet for at vælte siden', async () => {
    const fixture = TestBed.createComponent(SystemIntegrationsComponent);
    fixture.componentRef.setInput('systemId', 'sys-1');
    fixture.detectChanges();
    http.expectOne('/api/systems/sys-1/integrations').flush({ detail: 'Databasen svarer ikke.' }, { status: 500, statusText: 'Fejl' });
    await settle(fixture);

    expect(text(q(fixture, '[role="alert"]'))).toBe('Integrationerne kunne ikke hentes: Databasen svarer ikke.');
  });

  it('CSV-knappen henter systemets eksport med login og gemmer den under serverens filnavn', async () => {
    const f = await render(integrations([integrationItem()]));
    const createUrl = vi.fn().mockReturnValue('blob:x');
    const revoke = vi.fn();
    Object.assign(URL, { createObjectURL: createUrl, revokeObjectURL: revoke });
    const clicked: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this.download);
    });

    (q(f, '[data-testid="download"]') as HTMLButtonElement).click();
    const request = http.expectOne((r) => r.url === '/api/integrations/export.csv?systemId=sys-1');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['x']), {
      headers: { 'Content-Disposition': "attachment; filename=a.csv; filename*=UTF-8''integrationer-kompas-2026-09-24.csv" },
    });
    await settle(f);

    expect(clicked).toEqual(['integrationer-kompas-2026-09-24.csv']);
    expect(revoke).toHaveBeenCalledWith('blob:x');
  });
});
