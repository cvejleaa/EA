import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideDanishLocale } from '../core/locale';
import { provideRouter } from '@angular/router';
import type { SystemDetail } from '../api/types';
import { systemDetail, text, settle } from '../testing/fixtures';
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
    return fixture;
  }

  const q = (f: ComponentFixture<unknown>, selector: string) =>
    (f.nativeElement as HTMLElement).querySelector(selector);

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
});
