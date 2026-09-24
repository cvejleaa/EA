import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { IntegrationCreateRequest, IntegrationDto, IntegrationUpdateRequest, SystemListItem } from '../api/types';
import { AuthService } from '../core/auth.service';
import { provideDanishLocale } from '../core/locale';
import { integration, listItem, me, settle, systemDetail, systemLink, text } from '../testing/fixtures';
import { IntegrationFormPage } from './integration-form.page';

describe('IntegrationFormPage', () => {
  let http: HttpTestingController;
  const systems: SystemListItem[] = [
    listItem({ id: 'sys-1', name: 'Kompas' }),
    listItem({ id: 'lab', name: 'Laborant' }),
    listItem({ id: 'plat', name: 'Platformen', type: 'Platform' }),
    listItem({ id: 'hr', name: 'HR', parent: { id: 'n', name: 'Nordlys' } }),
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).me.set(me(true));
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  async function render(existing?: IntegrationDto): Promise<ComponentFixture<IntegrationFormPage>> {
    const fixture = TestBed.createComponent(IntegrationFormPage);
    fixture.componentRef.setInput('id', 'sys-1');
    if (existing) {
      fixture.componentRef.setInput('integrationId', existing.id);
    }
    fixture.detectChanges();
    http.expectOne('/api/systems/sys-1').flush(systemDetail());
    http.expectOne((r) => r.url === '/api/systems').flush({ items: systems, total: systems.length });
    http.expectOne('/api/data-objects').flush([{ id: 'd1', name: 'Medarbejder' }]);
    if (existing) {
      http.expectOne(`/api/integrations/${existing.id}`).flush(existing);
    }
    await settle(fixture);
    return fixture;
  }

  const c = (f: ComponentFixture<IntegrationFormPage>) => f.componentInstance;
  const el = (f: ComponentFixture<IntegrationFormPage>) => f.nativeElement as HTMLElement;
  const submit = async (f: ComponentFixture<IntegrationFormPage>) => {
    (el(f).querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await settle(f);
  };

  it.each([
    ['sender', 'sys-1', 'lab'],
    ['modtager', 'lab', 'sys-1'],
  ] as const)('retning "%s" bliver til fra=%s og til=%s (dataflow)', async (direction, from, to) => {
    const f = await render();
    c(f)['form'].controls.direction.setValue(direction);
    c(f)['pickOther'](systems[1]);
    c(f)['form'].patchValue({ viaPlatformId: 'plat', type: 'Api', name: ' Natlig ', dataObjectIds: ['d1'] });
    await submit(f);

    const request = http.expectOne((r) => r.method === 'POST' && r.url === '/api/integrations');
    expect(request.request.body as IntegrationCreateRequest).toEqual({
      fromSystemId: from,
      toSystemId: to,
      viaPlatformId: 'plat',
      type: 'Api',
      name: 'Natlig',
      description: null,
      dataObjectIds: ['d1'],
    });
    request.flush(integration());
    await settle(f);
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/systemer', 'sys-1']);
  });

  it('det andet system skal vælges fra listen — fritekst sendes ikke', async () => {
    const f = await render();
    c(f)['pickOther'](systems[1]);
    c(f)['onSearch']('Laborant og noget andet');
    await submit(f);

    http.expectNone((r) => r.method === 'POST');
    expect(text(el(f).querySelector('[data-testid="problem"]'))).toBe('Vælg det andet system fra listen.');
  });

  it('kan kun vælge platforme som "via" og aldrig systemet selv som anden ende', async () => {
    const f = await render();

    expect(el(f).querySelector('[data-testid="dir-sender"]')).not.toBeNull(); // modstykket til redigerings-testen
    expect(c(f)['platforms']().map((p) => p.name)).toEqual(['Platformen']);
    expect(c(f)['candidates']().map((s) => s.id)).toEqual(['lab', 'plat', 'hr']);
    c(f)['onSearch']('nordlys › h');
    expect(c(f)['candidates']().map((s) => s.id)).toEqual(['hr']);
  });

  it('redigering viser enderne skrivebeskyttet og sender version med', async () => {
    const existing = integration({
      from: systemLink('Kompas'),
      to: systemLink('Laborant'),
      via: systemLink('Platformen', { id: 'plat' }),
      type: 'Fil',
      version: 9,
    });
    const f = await render(existing);

    expect(text(el(f).querySelector('[data-testid="ends"]'))).toBe('Data sendes fra Kompas til Laborant.');
    expect(el(f).querySelector('[data-testid="dir-sender"]')).toBeNull();
    c(f)['form'].patchValue({ description: 'Ændret' });
    await submit(f);

    const put = http.expectOne('/api/integrations/int-1');
    expect(put.request.body as IntegrationUpdateRequest).toEqual({
      viaPlatformId: 'plat',
      type: 'Fil',
      name: null,
      description: 'Ændret',
      dataObjectIds: [],
      version: 9,
    });
    put.flush(existing);
    await settle(f);
  });

  it.each([
    ['urn:ea:problem:stale-version', true],
    ['urn:ea:problem:duplicate', false],
  ])('konflikt %s viser "Hent nyeste version": %s', async (type, showsReload) => {
    const f = await render(integration());
    await submit(f);
    http.expectOne('/api/integrations/int-1').flush({ type, detail: 'Konfliktbesked.' }, { status: 409, statusText: 'Conflict' });
    await settle(f);

    const problem = el(f).querySelector('[data-testid="problem"]');
    expect(text(problem?.querySelector('p') ?? null)).toBe('Konfliktbesked.');
    expect(!!problem?.querySelector('button')).toBe(showsReload);
  });

  it('sletning kræver bekræftelse og går tilbage til systemet', async () => {
    const f = await render(integration());
    (el(f).querySelector('[data-testid="delete"]') as HTMLButtonElement).click();
    await settle(f);
    http.expectNone((r) => r.method === 'DELETE');

    (el(f).querySelector('[data-testid="delete-confirm"]') as HTMLButtonElement).click();
    http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/integrations/int-1').flush(null);
    await settle(f);

    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/systemer', 'sys-1']);
  });

  it('viser ikke en tom formular, mens en eksisterende integration hentes', async () => {
    const fixture = TestBed.createComponent(IntegrationFormPage);
    fixture.componentRef.setInput('id', 'sys-1');
    fixture.componentRef.setInput('integrationId', 'int-1');
    fixture.detectChanges();
    http.expectOne('/api/systems/sys-1').flush(systemDetail());
    http.expectOne((r) => r.url === '/api/systems').flush({ items: systems, total: 4 });
    http.expectOne('/api/data-objects').flush([]);
    const pending = http.expectOne('/api/integrations/int-1');
    await settle(fixture);

    expect(text((fixture.nativeElement as HTMLElement).querySelector('[data-testid="loading"]'))).toBe('Henter…');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
    pending.flush(integration());
    await settle(fixture);
  });
});
