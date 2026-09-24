import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideDanishLocale } from '../core/locale';
import { Router, provideRouter } from '@angular/router';
import type { SystemListItem } from '../api/types';
import { AuthService } from '../core/auth.service';
import { capabilityNode, capabilityTree, listItem, me, text, settle } from '../testing/fixtures';
import { SystemListPage } from './system-list.page';

describe('SystemListPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const systemsRequest = (): TestRequest => http.expectOne((r) => r.url === '/api/systems');

  async function render(items: SystemListItem[], total: number, canEdit = false): Promise<ComponentFixture<SystemListPage>> {
    TestBed.inject(AuthService).me.set(me(canEdit));
    const fixture = TestBed.createComponent(SystemListPage);
    fixture.detectChanges();
    http.expectOne('/api/teams').flush([{ id: 'team-1', name: 'Stab' }]);
    await settle(fixture);
    systemsRequest().flush({ items, total });
    await settle(fixture);
    return fixture;
  }

  const rows = (f: ComponentFixture<unknown>) =>
    Array.from((f.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row')).map((r) =>
      Array.from(r.querySelectorAll('td')).map((c) => text(c)),
    );

  it('viser moduler under forælderen, aliastræf og forretningsejer med afdeling', async () => {
    const f = await render(
      [
        listItem({ id: 'a', name: 'Nordlys', moduleCount: 1 }),
        listItem({ id: 'd', name: 'Kompas', moduleCount: 3 }),
        listItem({
          id: 'b',
          name: 'HR',
          parent: { id: 'a', name: 'Nordlys' },
          businessOwner: { id: 'p', displayName: 'Hanne Holm', email: null, department: 'HR' },
        }),
        listItem({ id: 'c', name: 'Servicedesk', matchedAlias: 'Serviceportalen', managingTeam: { id: 't', name: 'Stab' } }),
      ],
      4,
    );

    const [nordlys, kompas, hr, servicedesk] = rows(f);
    expect(nordlys[0]).toBe('Nordlys · 1 modul');
    expect(kompas[0]).toBe('Kompas · 3 moduler');
    expect(hr[0]).toBe('Nordlys › HR');
    expect(hr[4]).toBe('Hanne Holm · HR');
    expect(nordlys[4]).toBe('Ikke angivet');
    expect(servicedesk[0]).toBe('Servicedesk (kendt som Serviceportalen)');
    expect(servicedesk[3]).toBe('Stab');
    expect(nordlys[3]).toBe('Ikke angivet');
  });

  it('viser antal uden filter og "viser X af Y" med filter', async () => {
    const f = await render([listItem(), listItem({ id: 'b', name: 'B' })], 2);
    expect(text((f.nativeElement as HTMLElement).querySelector('[data-testid="count"]'))).toBe('2 systemer');

    f.componentInstance['setFilter']('businessOwnerId', 'none');
    const request = systemsRequest();
    expect(request.request.params.get('businessOwnerId')).toBe('none');
    request.flush({ items: [listItem()], total: 2 });
    await settle(f);

    expect(text((f.nativeElement as HTMLElement).querySelector('[data-testid="count"]'))).toBe(
      'Viser 1 af 2 systemer Nulstil filtre',
    );
  });

  it('"Hent systemliste (CSV)" henter referencelisten og viser en fejl, hvis det ikke lykkes', async () => {
    const f = await render([listItem()], 1);
    Object.assign(URL, { createObjectURL: vi.fn().mockReturnValue('blob:s'), revokeObjectURL: vi.fn() });
    const clicked: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this.download);
    });
    const button = (f.nativeElement as HTMLElement).querySelector('[data-testid="download-systems"]') as HTMLButtonElement;

    button.click();
    http.expectOne('/api/systems/export.csv').flush(new Blob(['x'])); // uden Content-Disposition → reservenavnet
    await settle(f);
    expect(clicked).toEqual(['systemer.csv']);
    expect((f.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeNull();

    button.click();
    http.expectOne('/api/systems/export.csv').flush(new Blob(['x']), { status: 500, statusText: 'Fejl' });
    await settle(f);
    expect(text((f.nativeElement as HTMLElement).querySelector('[role="alert"]'))).toBe('Uventet fejl (500).');
    expect(clicked).toEqual(['systemer.csv']);
  });

  it('viser kun "Nyt system", når serveren giver lov', async () => {
    const reader = await render([], 0, false);
    expect(text(reader.nativeElement as HTMLElement)).not.toContain('Nyt system');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()] });
    http = TestBed.inject(HttpTestingController);
    const admin = await render([], 0, true);
    expect(text(admin.nativeElement as HTMLElement)).toContain('Nyt system');
  });

  describe('filter på kapabilitet', () => {
    /** Åbn listen med ?capabilityId=… (som et link fra kortet eller systemsiden) og besvar forespørgslerne. */
    async function openWith(capabilityId: string, answerTree?: (r: TestRequest) => void) {
      await TestBed.inject(Router).navigateByUrl(`/?capabilityId=${capabilityId}`);
      TestBed.inject(AuthService).me.set(me(false));
      const fixture = TestBed.createComponent(SystemListPage);
      fixture.detectChanges();
      http.expectOne('/api/teams').flush([]);
      await settle(fixture);
      const systems = systemsRequest();
      if (answerTree) {
        answerTree(http.expectOne('/api/capabilities'));
      }
      systems.flush({ items: [listItem()], total: 5 });
      await settle(fixture);
      return { fixture, systems };
    }

    const selected = (f: ComponentFixture<unknown>) =>
      text((f.nativeElement as HTMLElement).querySelector('[data-testid="capability-filter"] .mat-mdc-select-value'));

    it('filtrerer på kapabiliteten fra linket og viser dens navn', async () => {
      const { fixture, systems } = await openWith('cap-K1.1', (r) =>
        r.flush(capabilityTree([capabilityNode('K1', 0), capabilityNode('K1.1', 1, { name: 'Optagelse', selectable: true })])),
      );

      expect(systems.request.params.get('capabilityId')).toBe('cap-K1.1');
      expect(selected(fixture)).toBe('K1.1 Optagelse');
    });

    it('en udgået kapabilitet findes på listen over koblinger, der bør flyttes', async () => {
      const { fixture } = await openWith('cap-K9', (r) =>
        r.flush({
          ...capabilityTree([capabilityNode('K1', 0)]),
          toMove: [{ id: 'cap-K9', code: 'K9', name: 'Gammel eksamen', path: '', reason: 'Udgaaet', systems: [] }],
        }),
      );

      expect(selected(fixture)).toBe('K9 Gammel eksamen');
    });

    it('kan navnet ikke hentes, filtreres der alligevel', async () => {
      const { fixture, systems } = await openWith('cap-K1', (r) =>
        r.flush({ detail: 'Nej' }, { status: 500, statusText: 'Fejl' }),
      );

      expect(systems.request.params.get('capabilityId')).toBe('cap-K1');
      expect(selected(fixture)).toBe('Valgt kapabilitet');
      expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeNull();
    });

    it('"Ikke angivet" sender none og henter ikke kortet', async () => {
      const { fixture, systems } = await openWith('none');

      expect(systems.request.params.get('capabilityId')).toBe('none');
      expect(selected(fixture)).toBe('Ikke angivet (nedlagte undtaget)');
      // afterEach(http.verify) fejler, hvis kortet blev hentet.
    });
  });
});
