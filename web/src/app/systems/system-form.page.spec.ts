import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideDanishLocale } from '../core/locale';
import type { SystemDetail, SystemWriteRequest } from '../api/types';
import { AuthService } from '../core/auth.service';
import { me, systemDetail, text, settle } from '../testing/fixtures';
import { SystemFormPage } from './system-form.page';

describe('SystemFormPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).me.set(me(true));
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  async function render(existing?: SystemDetail): Promise<ComponentFixture<SystemFormPage>> {
    const fixture = TestBed.createComponent(SystemFormPage);
    if (existing) {
      fixture.componentRef.setInput('id', existing.id);
    }
    fixture.detectChanges();
    http.expectOne('/api/teams').flush([{ id: 'team-1', name: 'Stab' }]);
    http.expectOne('/api/persons').flush([
      { id: 'p-bo', displayName: 'Bo Bogholder', email: null, department: 'Økonomi' },
      { id: 'p-fr', displayName: 'Frida', email: null, department: null },
    ]);
    http.expectOne((r) => r.url === '/api/systems/parent-candidates').flush([{ id: 'parent-1', name: 'Nordlys' }]);
    if (existing) {
      http.expectOne(`/api/systems/${existing.id}`).flush(existing);
    }
    await settle(fixture);
    return fixture;
  }

  const form = (f: ComponentFixture<SystemFormPage>) => f.componentInstance['form'];
  const submit = async (f: ComponentFixture<SystemFormPage>) => {
    ((f.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await settle(f);
  };

  it('viser ikke en tom formular, mens et eksisterende system hentes', async () => {
    const fixture = TestBed.createComponent(SystemFormPage);
    fixture.componentRef.setInput('id', 'sys-1');
    fixture.detectChanges();
    http.expectOne('/api/teams').flush([]);
    http.expectOne('/api/persons').flush([]);
    http.expectOne((r) => r.url === '/api/systems/parent-candidates').flush([]);
    const pending = http.expectOne('/api/systems/sys-1');
    await settle(fixture);

    const el = fixture.nativeElement as HTMLElement;
    expect(text(el.querySelector('[data-testid="loading"]'))).toBe('Henter…');
    expect(el.querySelector('button[type="submit"]')).toBeNull();

    pending.flush(systemDetail());
    await settle(fixture);
    expect(el.querySelector('[data-testid="loading"]')).toBeNull();
    expect(form(fixture).controls.name.value).toBe('Kompas');
  });

  it('status har ingen standardværdi og skal vælges, før der sendes noget', async () => {
    const f = await render();
    expect(form(f).controls.lifecycleStatus.value).toBeNull();

    form(f).controls.name.setValue('Nyt system');
    await submit(f);

    http.expectNone((r) => r.method === 'POST');
    expect(text((f.nativeElement as HTMLElement).querySelector('[data-testid="problem"]'))).toBe('Udfyld de markerede felter.');
  });

  it('sender forretningsejer, forvaltere og aliaser som roller og liste', async () => {
    const f = await render();
    form(f).patchValue({
      name: ' Laborant ',
      aliases: 'Kemi, , Lab ',
      lifecycleStatus: 'Planlagt',
      businessOwnerId: 'p-bo',
      stewardIds: ['p-fr'],
      parentSystemId: 'parent-1',
    });
    await submit(f);

    const request = http.expectOne((r) => r.method === 'POST' && r.url === '/api/systems');
    const body = request.request.body as SystemWriteRequest;
    expect(body).toEqual({
      name: ' Laborant ',
      aliases: ['Kemi', 'Lab'],
      description: null,
      type: null,
      lifecycleStatus: 'Planlagt',
      managingTeamId: null,
      parentSystemId: 'parent-1',
      roles: [
        { role: 'Forretningsejer', personId: 'p-bo' },
        { role: 'Systemforvalter', personId: 'p-fr' },
      ],
      version: null,
    });
    request.flush(systemDetail({ id: 'ny' }));
    await settle(f);
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/systemer', 'ny']);
  });

  it('ved konflikt bevares indtastningen, og brugeren kan hente den nyeste version', async () => {
    const f = await render(systemDetail({ version: 7 }));
    form(f).controls.description.setValue('Min ændring');
    await submit(f);

    const put = http.expectOne('/api/systems/sys-1');
    expect((put.request.body as SystemWriteRequest).version).toBe(7);
    put.flush(
      { type: 'urn:ea:problem:stale-version', detail: 'Systemet er ændret af en anden, siden du åbnede det.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(f);

    const problem = (f.nativeElement as HTMLElement).querySelector('[data-testid="problem"]');
    expect(text(problem)).toContain('Systemet er ændret af en anden, siden du åbnede det.');
    expect(text(problem)).toContain('Hent nyeste version (dine ændringer kasseres)');
    expect(form(f).controls.description.value).toBe('Min ændring');
  });

  it('forælder-feltet er låst med forklaring for et system med moduler', async () => {
    const reason = 'Systemet har selv moduler og kan derfor ikke gøres til modul.';
    const f = await render(
      systemDetail({ permissions: { canEdit: true, canDelete: false, deleteBlockedReason: 'x', parentBlockedReason: reason } }),
    );
    expect(form(f).controls.parentSystemId.disabled).toBe(true);
    expect(text((f.nativeElement as HTMLElement).querySelector('[data-testid="parent-blocked"]'))).toBe(reason);
  });

  it('viser serverens feltfejl ved roller', async () => {
    const f = await render();
    form(f).patchValue({ name: 'X', lifecycleStatus: 'IDrift' });
    await submit(f);
    http
      .expectOne((r) => r.method === 'POST')
      .flush(
        { title: 'Der er fejl i oplysningerne.', errors: { roles: ['Et system kan kun have én forretningsejer.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await settle(f);

    expect(text(f.nativeElement as HTMLElement)).toContain('Et system kan kun have én forretningsejer.');
  });

  it('en valideringsfejl ved redigering tilbyder ikke at kassere ændringerne', async () => {
    const f = await render(systemDetail());
    await submit(f);
    http
      .expectOne('/api/systems/sys-1')
      .flush(
        { title: 'Der er fejl i oplysningerne.', errors: { roles: ['Et system kan kun have én forretningsejer.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await settle(f);

    const problem = (f.nativeElement as HTMLElement).querySelector('[data-testid="problem"]');
    expect(text(problem)).toBe('Der er fejl i oplysningerne.');
    // Kun en forældet version må vise "Hent nyeste version (dine ændringer kasseres)".
    expect(problem?.querySelector('button')).toBeNull();
  });

  it('en navnedublet (409) tilbyder heller ikke at kassere ændringerne', async () => {
    const f = await render(systemDetail());
    await submit(f);
    http
      .expectOne('/api/systems/sys-1')
      .flush(
        { type: 'urn:ea:problem:duplicate', detail: 'Der findes allerede et system med navnet "Kompas".' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle(f);

    const problem = (f.nativeElement as HTMLElement).querySelector('[data-testid="problem"]');
    expect(text(problem)).toBe('Der findes allerede et system med navnet "Kompas".');
    expect(problem?.querySelector('button')).toBeNull();
  });
});
