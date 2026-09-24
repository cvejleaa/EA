import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideDanishLocale } from '../core/locale';
import { provideRouter } from '@angular/router';
import type { SystemListItem } from '../api/types';
import { AuthService } from '../core/auth.service';
import { listItem, me, text, settle } from '../testing/fixtures';
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

  it('viser kun "Nyt system", når serveren giver lov', async () => {
    const reader = await render([], 0, false);
    expect(text(reader.nativeElement as HTMLElement)).not.toContain('Nyt system');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()] });
    http = TestBed.inject(HttpTestingController);
    const admin = await render([], 0, true);
    expect(text(admin.nativeElement as HTMLElement)).toContain('Nyt system');
  });
});
