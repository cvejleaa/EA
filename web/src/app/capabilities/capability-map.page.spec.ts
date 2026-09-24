import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { CapabilityTreeResponse } from '../api/types';
import { provideDanishLocale } from '../core/locale';
import { capabilityNode, capabilityTree, settle, text } from '../testing/fixtures';
import { CapabilityMapPage } from './capability-map.page';

describe('CapabilityMapPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideDanishLocale(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(
    response: CapabilityTreeResponse,
  ): Promise<ComponentFixture<CapabilityMapPage>> {
    const fixture = TestBed.createComponent(CapabilityMapPage);
    fixture.detectChanges();
    http.expectOne('/api/capabilities').flush(response);
    await settle(fixture);
    return fixture;
  }

  const el = (f: ComponentFixture<unknown>) => f.nativeElement as HTMLElement;
  const q = (f: ComponentFixture<unknown>, selector: string) => el(f).querySelector(selector);

  it('viser hele træet udfoldet med kode, navn, beskrivelse og indrykning efter niveau', async () => {
    const f = await render(
      capabilityTree([
        capabilityNode('K1', 0, { name: 'Uddannelse' }),
        capabilityNode('K1.1', 1, {
          name: 'Studieadministration',
          description: 'Fra ansøgning til bevis',
        }),
        capabilityNode('K1.1.1', 2, { name: 'Optagelse' }),
        capabilityNode('K2', 0, { name: 'Forskning' }),
      ]),
    );

    const items = Array.from(el(f).querySelectorAll('[data-testid="tree"] li'));
    expect(
      items.map((li) => [
        text(li.querySelector('.code')),
        text(li.querySelector('.name')),
        text(li.querySelector('.description')),
      ]),
    ).toEqual([
      ['K1', 'Uddannelse', ''],
      ['K1.1', 'Studieadministration', 'Fra ansøgning til bevis'],
      ['K1.1.1', 'Optagelse', ''],
      ['K2', 'Forskning', ''],
    ]);
    expect(items.map((li) => (li as HTMLElement).style.paddingLeft)).toEqual([
      '0px',
      '24px',
      '48px',
      '0px',
    ]);
    expect(items.map((li) => li.classList.contains('top'))).toEqual([true, false, false, true]);
    expect(text(q(f, '[data-testid="count"]'))).toBe(
      '4 kapabiliteter · kortet ændres kun ved import',
    );
    expect(q(f, '[data-testid="to-move"]')).toBeNull();
  });

  it('kun kapabiliteter, der kan kobles til, linker til systemlisten', async () => {
    const f = await render(
      capabilityTree([
        capabilityNode('K1', 0, { name: 'Uddannelse' }),
        capabilityNode('K1.1', 1, { name: 'Optagelse', selectable: true }),
      ]),
    );

    // En familie eller gruppe har ingen koblinger — et link dertil ville altid vise nul systemer.
    const items = Array.from(el(f).querySelectorAll('[data-testid="tree"] li'));
    expect(items.map((li) => li.querySelector('a.name')?.getAttribute('href') ?? null)).toEqual([
      null,
      '/systemer?capabilityId=cap-K1.1',
    ]);
    expect(text(q(f, '[data-testid="hint"]'))).toBe(
      'Systemer kobles til kapabiliteterne på nederste niveau. Klik på én for at se de systemer, der er koblet til den.',
    );
  });

  it('arbejdslisten viser koblinger, der bør flyttes, med grund og systemer', async () => {
    const f = await render({
      ...capabilityTree([capabilityNode('K1', 0, { name: 'Uddannelse' })]),
      toMove: [
        {
          id: 'cap-K9',
          code: 'K9',
          name: 'Gammel eksamen',
          path: 'Uddannelse',
          reason: 'Udgaaet',
          systems: [
            { id: 'sys-a', name: 'Kompas' },
            { id: 'sys-b', name: 'Nordlys › HR' },
          ],
        },
        { id: 'cap-K1', code: 'K1', name: 'Uddannelse', path: '', reason: 'HarUnderkapabiliteter', systems: [{ id: 'sys-c', name: 'Laborant' }] },
      ],
    });

    const section = q(f, '[data-testid="to-move"]')!;
    expect(text(section.querySelector('h2'))).toBe('Koblinger, der bør flyttes (2)');
    expect(Array.from(section.querySelectorAll('li')).map((li) => text(li))).toEqual([
      'K9 Gammel eksamen · Uddannelse — Udgået af kortet — flyt koblingen Systemer: Kompas, Nordlys › HR',
      'K1 Uddannelse — Har fået underkapabiliteter — vælg den, der passer bedst Systemer: Laborant',
    ]);
    expect(Array.from(section.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual([
      '/systemer/sys-a',
      '/systemer/sys-b',
      '/systemer/sys-c',
    ]);
  });

  it('import-knappen vises kun, når serveren giver lov', async () => {
    const admin = await render(capabilityTree([capabilityNode('K1', 0)], true));
    expect(q(admin, '[data-testid="import"]')?.getAttribute('href')).toBe('/kapabiliteter/import');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideDanishLocale(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const reader = await render(capabilityTree([capabilityNode('K1', 0)], false));
    expect(q(reader, '[data-testid="import"]')).toBeNull();
    expect(q(reader, '[data-testid="download"]')).not.toBeNull(); // Alle kan hente kortet.
  });

  it.each([
    [
      true,
      "Kapabilitetskortet er ikke indlæst endnu. Importér DTU's kort fra en CSV-fil med knappen ovenfor.",
    ],
    [false, 'Kapabilitetskortet er ikke indlæst endnu. Enterprise arkitekten indlæser det.'],
  ])('et tomt kort siger det ærligt (må importere: %s)', async (canImport, message) => {
    const f = await render(capabilityTree([], canImport));

    expect(text(q(f, '[data-testid="empty"]'))).toBe(message);
    expect(q(f, '[data-testid="tree"]')).toBeNull();
    expect(q(f, '[data-testid="download"]')).toBeNull();
    expect(q(f, '[data-testid="download-couplings"]')).toBeNull();
  });

  it('en fejl vises i stedet for et tomt kort', async () => {
    const fixture = TestBed.createComponent(CapabilityMapPage);
    fixture.detectChanges();
    http
      .expectOne('/api/capabilities')
      .flush({ detail: 'Databasen svarer ikke.' }, { status: 500, statusText: 'Fejl' });
    await settle(fixture);

    expect(text(q(fixture, '[role="alert"]'))).toBe(
      'Kortet kunne ikke hentes: Databasen svarer ikke.',
    );
    expect(q(fixture, '[data-testid="empty"]')).toBeNull();
  });

  it('en fejl ved hentning af CSV vises, og kortet står der stadig', async () => {
    const f = await render(capabilityTree([capabilityNode('K1', 0)]));

    (q(f, '[data-testid="download"]') as HTMLButtonElement).click();
    http
      .expectOne('/api/capabilities/export.csv')
      .flush(new Blob(['x']), { status: 500, statusText: 'Fejl' });
    await settle(f);

    expect(text(q(f, '[role="alert"]'))).toBe('Uventet fejl (500).');
    expect(q(f, '[data-testid="tree"]')).not.toBeNull();
  });

  it('"Hent koblinger (CSV)" henter koblings-eksporten, og begge knapper er spærret imens', async () => {
    const f = await render(capabilityTree([capabilityNode('K1', 0)]));
    Object.assign(URL, {
      createObjectURL: vi.fn().mockReturnValue('blob:c'),
      revokeObjectURL: vi.fn(),
    });
    const clicked: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      clicked.push(this.download);
    });

    (q(f, '[data-testid="download-couplings"]') as HTMLButtonElement).click();
    await settle(f);
    expect((q(f, '[data-testid="download"]') as HTMLButtonElement).disabled).toBe(true);
    expect((q(f, '[data-testid="download-couplings"]') as HTMLButtonElement).disabled).toBe(true);
    http.expectOne('/api/capabilities/couplings/export.csv').flush(new Blob(['x']), {
      headers: { 'Content-Disposition': "attachment; filename*=UTF-8''koblinger-2026-09-24.csv" },
    });
    await settle(f);

    expect(clicked).toEqual(['koblinger-2026-09-24.csv']);
    expect((q(f, '[data-testid="download-couplings"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('"Hent kortet (CSV)" henter eksporten under serverens filnavn', async () => {
    const f = await render(capabilityTree([capabilityNode('K1', 0)]));
    Object.assign(URL, {
      createObjectURL: vi.fn().mockReturnValue('blob:k'),
      revokeObjectURL: vi.fn(),
    });
    const clicked: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      clicked.push(this.download);
    });

    (q(f, '[data-testid="download"]') as HTMLButtonElement).click();
    http.expectOne('/api/capabilities/export.csv').flush(new Blob(['x']), {
      headers: {
        'Content-Disposition': "attachment; filename*=UTF-8''kapabiliteter-2026-09-24.csv",
      },
    });
    await settle(f);

    expect(clicked).toEqual(['kapabiliteter-2026-09-24.csv']);
  });
});
