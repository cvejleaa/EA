import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideDanishLocale } from '../core/locale';
import { provideRouter } from '@angular/router';
import type { SystemDetail } from '../api/types';
import { integrations, systemCapability, systemDetail, text, settle } from '../testing/fixtures';
import { SystemDetailPage } from './system-detail.page';

describe('SystemDetailPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(s: SystemDetail): Promise<ComponentFixture<SystemDetailPage>> {
    const fixture = TestBed.createComponent(SystemDetailPage);
    fixture.componentRef.setInput('id', s.id);
    fixture.detectChanges();
    http.expectOne(`/api/systems/${s.id}`).flush(s);
    await settle(fixture);
    // Integrationssektionen henter sine egne data (testet for sig i system-integrations.component.spec.ts).
    http.expectOne(`/api/systems/${s.id}/integrations`).flush(integrations());
    await settle(fixture);
    return fixture;
  }

  const q = (f: ComponentFixture<unknown>, selector: string) =>
    (f.nativeElement as HTMLElement).querySelector(selector);

  it('viser egne kapabiliteter først, familiens med "via", og markerer koblinger, der bør flyttes', async () => {
    const nordlys = { id: 'sys-par', name: 'Nordlys' };
    const f = await render(
      systemDetail({
        parent: nordlys,
        capabilities: [
          systemCapability('K1.1.1', null, { name: 'Optagelse', path: 'Uddannelse › Studieadministration' }),
          systemCapability('K1.9', null, { name: 'Gammel eksamen', path: 'Uddannelse', moveReason: 'Udgaaet' }),
          systemCapability('K2', nordlys, { name: 'Forskning', moveReason: 'HarUnderkapabiliteter' }),
          systemCapability('K3.1', { id: 'sys-mod', name: 'Laboratorie' }, { name: 'Prøver', path: 'Laboratorier' }),
        ],
      }),
    );

    const items = Array.from(q(f, '[data-testid="capabilities"]')!.querySelectorAll('li'));
    expect(items.map((li) => text(li))).toEqual([
      'K1.1.1 Optagelse · Uddannelse › Studieadministration',
      'K1.9 Gammel eksamen · Uddannelse Udgået af kortet — flyt koblingen',
      'K2 Forskning · via forælderen Nordlys Har fået underkapabiliteter — vælg den, der passer bedst',
      'K3.1 Prøver · Laboratorier · via modulet Laboratorie',
    ]);
    expect(items.map((li) => li.querySelector('[data-testid="move-reason"]') !== null)).toEqual([false, true, true, false]);
    expect(items.map((li) => li.classList.contains('held'))).toEqual([false, false, true, true]);
    // Hver kobling fører til systemlisten filtreret på kapabiliteten.
    expect(items[0].querySelector('a')?.getAttribute('href')).toBe('/systemer?capabilityId=cap-K1.1.1');
    expect(q(f, '[data-testid="capabilities-none"]')).toBeNull();
  });

  it('siger "Ikke angivet", når hverken systemet eller familien har kapabiliteter', async () => {
    const f = await render(systemDetail({ capabilities: [] }));
    expect(text(q(f, '[data-testid="capabilities-none"]'))).toBe('Ikke angivet');
    expect(q(f, '[data-testid="capabilities"]')).toBeNull();
  });

  it('viser forretningsejer med afdeling', async () => {
    const f = await render(
      systemDetail({
        roles: [
          { role: 'Forretningsejer', person: { id: 'p1', displayName: 'Bo Bogholder', email: null, department: 'Økonomi' } },
          { role: 'Systemforvalter', person: { id: 'p2', displayName: 'Frida', email: null, department: null } },
        ],
      }),
    );
    expect(text(q(f, '[data-testid="business-owner"]'))).toBe('Bo Bogholder · Økonomi');
  });

  it('siger "Ikke angivet", når forretningsejeren mangler', async () => {
    const f = await render(systemDetail({ roles: [] }));
    expect(text(q(f, '[data-testid="business-owner"]'))).toBe('Ikke angivet');
  });

  it('viser redigering, bekræftelse og sletning, når serveren siger canEdit', async () => {
    const f = await render(systemDetail());
    expect(q(f, '[data-testid="edit"]')).not.toBeNull();
    expect(q(f, '[data-testid="confirm"]')).not.toBeNull();
    expect((q(f, '[data-testid="delete"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('skjuler alle handlinger for en læser', async () => {
    const f = await render(
      systemDetail({ permissions: { canEdit: false, canDelete: false, deleteBlockedReason: null, parentBlockedReason: null } }),
    );
    expect(q(f, '[data-testid="edit"]')).toBeNull();
    expect(q(f, '[data-testid="confirm"]')).toBeNull();
    expect(q(f, '[data-testid="delete"]')).toBeNull();
  });

  it('deaktiverer sletning og forklarer hvorfor, når systemet har moduler', async () => {
    const reason = 'Nordlys har moduler (3) — flyt eller slet dem først.';
    const f = await render(
      systemDetail({ permissions: { canEdit: true, canDelete: false, deleteBlockedReason: reason, parentBlockedReason: null } }),
    );
    expect((q(f, '[data-testid="delete"]') as HTMLButtonElement).disabled).toBe(true);
    expect(text(q(f, '[data-testid="delete-blocked"]'))).toBe(reason);
  });

  it('"Bekræft uændret" sender den viste version og viser den nye bekræftelse', async () => {
    const f = await render(systemDetail({ version: 7 }));
    (q(f, '[data-testid="confirm"]') as HTMLButtonElement).click();

    const request = http.expectOne('/api/systems/sys-1/confirm');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ version: 7 });
    request.flush(systemDetail({ version: 8, lastConfirmedAt: '2026-09-20T10:00:00Z', lastConfirmedByName: 'Frida Forvalter' }));
    await settle(f);

    expect(text(q(f, '[data-testid="confirmed"]'))).toContain('20. sep. 2026 af Frida Forvalter');
  });

  it('viser serverens besked, hvis bekræftelsen afvises', async () => {
    const f = await render(systemDetail());
    (q(f, '[data-testid="confirm"]') as HTMLButtonElement).click();
    http
      .expectOne('/api/systems/sys-1/confirm')
      .flush({ detail: 'Systemet er ændret af en anden.' }, { status: 409, statusText: 'Conflict' });
    await settle(f);

    expect(text(q(f, '[role="alert"]'))).toBe('Systemet er ændret af en anden.');
  });

  describe('overlap på systemsiden', () => {
    it('"Deles med" er neutral information med links, og det siges, når en kobling ikke tæller med', async () => {
      const loen = { id: 'sys-l', name: 'Løn' };
      const own = systemCapability('K1.1', null, { name: 'Optagelse' });
      const viaModule = systemCapability('K2.1', loen, { name: 'Løn og personale' });
      const f = await render(
        systemDetail({
          modules: [{ id: loen.id, name: loen.name, lifecycleStatus: 'IDrift' }],
          capabilities: [
            {
              ...own,
              ownExclusion: 'Udfases',
              sharedWith: [
                { id: 'sys-k', name: 'Kompas', exclusion: null },
                { id: 'sys-u', name: 'Ugle', exclusion: 'Udfases' },
              ],
            },
            { ...viaModule, ownExclusion: 'LokalLoesning', sharedWith: [] },
          ],
        }),
      );

      const items = Array.from(q(f, '[data-testid="capabilities"]')!.querySelectorAll('li'));
      const shared = items[0].querySelector('[data-testid="shared-with"]')!;
      expect(text(shared)).toBe('Deles med: Kompas, Ugle (udfases)');
      expect(Array.from(shared.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual([
        '/systemer/sys-k',
        '/systemer/sys-u',
      ]);
      // Neutral, ikke en fejl: en forvalter må ikke fristes til at fjerne en korrekt kobling.
      expect(shared.classList.contains('muted')).toBe(true);
      expect(q(f, '[role="alert"]')).toBeNull();
      expect(text(items[0].querySelector('[data-testid="own-exclusion"]'))).toBe(
        'Dette system tæller ikke med i overlap (udfases).',
      );
      expect(items[1].querySelector('[data-testid="shared-with"]')).toBeNull();
      expect(text(items[1].querySelector('[data-testid="own-exclusion"]'))).toBe(
        'Løn tæller ikke med i overlap (lokal løsning/udtræk).',
      );
      expect(text(q(f, '[data-testid="shared-hint"]'))).toBe(
        '"Deles med" er andre systemer, der er koblet til samme kapabilitet. Et system og dets moduler tæller som ét system.',
      );
    });

    it('uden andre systemer på kapabiliteterne er der hverken "Deles med" eller forklaring', async () => {
      const f = await render(systemDetail({ capabilities: [systemCapability('K1.1')] }));

      expect(q(f, '[data-testid="shared-with"]')).toBeNull();
      expect(q(f, '[data-testid="own-exclusion"]')).toBeNull();
      expect(q(f, '[data-testid="shared-hint"]')).toBeNull();
    });
  });
});
