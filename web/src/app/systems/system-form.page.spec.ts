import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideDanishLocale } from '../core/locale';
import type { CapabilityTreeResponse, SystemDetail, SystemWriteRequest } from '../api/types';
import { AuthService } from '../core/auth.service';
import {
  capabilityNode,
  capabilityTree,
  me,
  settle,
  systemCapability,
  systemDetail,
  text,
} from '../testing/fixtures';
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

  async function render(
    existing?: SystemDetail,
    tree: CapabilityTreeResponse = capabilityTree([]),
  ): Promise<ComponentFixture<SystemFormPage>> {
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
    http.expectOne('/api/capabilities').flush(tree);
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
    http.expectOne('/api/capabilities').flush(capabilityTree([]));
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
      // En tom liste, ikke null: null betyder "uændret" på serveren.
      capabilityIds: [],
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

  it('et låst forælder-felt sender den nuværende forælder med — ellers ville et gem være en flytning', async () => {
    const reason = 'Modulet kan kun flyttes af den, der kan redigere Nordlys.';
    const f = await render(
      systemDetail({
        parent: { id: 'parent-1', name: 'Nordlys' },
        permissions: { canEdit: true, canDelete: false, deleteBlockedReason: 'x', parentBlockedReason: reason },
      }),
    );
    expect(form(f).controls.parentSystemId.disabled).toBe(true);
    expect(text((f.nativeElement as HTMLElement).querySelector('[data-testid="parent-blocked"]'))).toBe(reason);

    form(f).patchValue({ description: 'Ny tekst' });
    await submit(f);
    const request = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/systems/sys-1');
    expect((request.request.body as SystemWriteRequest).parentSystemId).toBe('parent-1');
    request.flush(systemDetail());
    await settle(f);
  });

  it('en forvalter, der ikke må oprette personer, får at vide, hvem der kan', async () => {
    TestBed.inject(AuthService).me.set(me(false));
    const f = await render(systemDetail());
    const root = f.nativeElement as HTMLElement;
    const addPerson = () => Array.from(root.querySelectorAll('button')).some((b) => text(b) === 'Personen findes ikke på listen?');

    expect(text(root.querySelector('[data-testid="person-missing-hint"]'))).toBe(
      'Mangler personen på listen? Kontakt enterprise arkitekten.',
    );
    expect(addPerson()).toBe(false);

    TestBed.inject(AuthService).me.set(me(true));
    await settle(f);
    expect(root.querySelector('[data-testid="person-missing-hint"]')).toBeNull();
    expect(addPerson()).toBe(true);
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

  describe('kapabiliteter', () => {
    const tree = capabilityTree([
      capabilityNode('K1', 0, { name: 'Uddannelse' }),
      capabilityNode('K1.1', 1, { name: 'Studieadministration', path: 'Uddannelse' }),
      capabilityNode('K1.1.1', 2, { name: 'Optagelse', path: 'Uddannelse › Studieadministration', selectable: true }),
      capabilityNode('K1.1.2', 2, { name: 'Eksamen', path: 'Uddannelse › Studieadministration', selectable: true }),
      capabilityNode('K1.2', 1, { name: 'Studievejledning', path: 'Uddannelse', selectable: true }),
      capabilityNode('K2', 0, { name: 'Forskning' }),
      capabilityNode('K2.1', 1, { name: 'Bevillinger', path: 'Forskning', selectable: true }),
    ]);
    const existing = () =>
      systemDetail({
        capabilities: [
          systemCapability('K1.1.1', null, { name: 'Optagelse', path: 'Uddannelse › Studieadministration' }),
          systemCapability('K1.9', null, { name: 'Gammel eksamen', path: 'Uddannelse', moveReason: 'Udgaaet' }),
          systemCapability('K3', null, { name: 'Støtte', moveReason: 'HarUnderkapabiliteter' }),
          systemCapability('K2.1', { id: 'sys-mod', name: 'Laboratorie' }, { name: 'Bevillinger', path: 'Forskning' }),
        ],
      });

    const el = (f: ComponentFixture<SystemFormPage>) => f.nativeElement as HTMLElement;
    const chips = (f: ComponentFixture<SystemFormPage>) =>
      Array.from(el(f).querySelectorAll('[data-testid="capability-chips"] mat-chip-row')).map((c) => text(c));

    /** Skriv i søgefeltet og returnér de tilbudte valgmuligheder (autocomplete ligger i et overlay). */
    async function search(f: ComponentFixture<SystemFormPage>, query: string): Promise<HTMLElement[]> {
      const input = el(f).querySelector('[data-testid="capability-search"]') as HTMLInputElement;
      input.dispatchEvent(new Event('focusin'));
      input.value = query;
      input.dispatchEvent(new Event('input'));
      await settle(f);
      return Array.from(document.querySelectorAll<HTMLElement>('mat-option'));
    }

    it('viser egne koblinger som valgte — med markering — og familiens for sig', async () => {
      const f = await render(existing(), tree);

      expect(chips(f)).toEqual([
        'K1.1.1 Optagelse ×',
        'K1.9 Gammel eksamen · udgået ×',
        'K3 Støtte · har underkapabiliteter ×',
      ]);
      const rows = Array.from(el(f).querySelectorAll('[data-testid="capability-chips"] mat-chip-row'));
      expect(rows.map((r) => r.classList.contains('flagged'))).toEqual([false, true, true]);
      expect(text(el(f).querySelector('[data-testid="family-capabilities"]'))).toBe(
        'Kapabiliteter via forælder eller moduler (redigeres på det andet system): K2.1 Bevillinger — via modulet Laboratorie',
      );
    });

    it('søger på kode, navn og gruppe, men tilbyder kun blade, der ikke allerede er valgt', async () => {
      const f = await render(existing(), tree);

      // Gruppens navn finder bladene under den; K1.1.1 er valgt, og gruppen K1.1 selv kan ikke vælges.
      expect((await search(f, 'studieadm')).map((o) => text(o))).toEqual([
        'K1.1.2 Eksamen · Uddannelse › Studieadministration',
      ]);
      expect((await search(f, 'k1.2')).map((o) => text(o))).toEqual(['K1.2 Studievejledning · Uddannelse']);
      expect((await search(f, 'uddannelse')).map((o) => text(o))).toEqual([
        'K1.1.2 Eksamen · Uddannelse › Studieadministration',
        'K1.2 Studievejledning · Uddannelse',
      ]);
    });

    it('sender HELE listen af egne koblinger — tilføjet og fjernet — men aldrig familiens', async () => {
      const f = await render(existing(), tree);

      const [eksamen] = await search(f, 'eksamen');
      eksamen.click();
      await settle(f);
      expect((el(f).querySelector('[data-testid="capability-search"]') as HTMLInputElement).value).toBe('');
      (el(f).querySelector('[data-testid="remove-capability"]') as HTMLButtonElement).click(); // K1.1.1
      await settle(f);
      expect(chips(f)).toEqual([
        'K1.9 Gammel eksamen · udgået ×',
        'K3 Støtte · har underkapabiliteter ×',
        'K1.1.2 Eksamen ×',
      ]);

      await submit(f);
      const put = http.expectOne('/api/systems/sys-1');
      // Koblinger, der bør flyttes, sendes med: de bevares, indtil nogen flytter dem.
      expect((put.request.body as SystemWriteRequest).capabilityIds).toEqual(['cap-K1.9', 'cap-K3', 'cap-K1.1.2']);
      put.flush(systemDetail());
      await settle(f);
    });

    it('tilbyder højst 50 forslag ad gangen', async () => {
      const leaves = Array.from({ length: 60 }, (_, i) =>
        capabilityNode(`B${i + 1}`, 1, { name: `Blad ${i + 1}`, path: 'Stor gruppe', selectable: true }),
      );
      const f = await render(undefined, capabilityTree([capabilityNode('B', 0, { name: 'Stor gruppe' }), ...leaves]));

      const options = await search(f, 'stor gruppe');
      // 60 blade matcher; de første 50 i kortets rækkefølge vises.
      expect(options.length).toBe(50);
      expect(text(options[49])).toBe('B50 Blad 50 · Stor gruppe');
    });

    it('viser serverens fejl ved kapabiliteterne ved feltet', async () => {
      const f = await render(existing(), tree);
      await submit(f);
      http
        .expectOne('/api/systems/sys-1')
        .flush(
          { title: 'Der er fejl i oplysningerne.', errors: { capabilityIds: ['K1.1 har underkapabiliteter og kan ikke vælges.'] } },
          { status: 400, statusText: 'Bad Request' },
        );
      await settle(f);

      expect(Array.from(el(f).querySelectorAll('[data-testid="capability-error"]')).map((e) => text(e))).toEqual([
        'K1.1 har underkapabiliteter og kan ikke vælges.',
      ]);
    });
  });
});
