import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { CapabilityOverlap, CapabilityTreeResponse } from '../api/types';
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
    expect(q(admin, '[data-testid="import-couplings"]')?.getAttribute('href')).toBe('/kapabiliteter/koblinger/import');

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
    expect(q(reader, '[data-testid="import-couplings"]')).toBeNull();
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

  describe('overlap og dækning', () => {
    const overlap = (o: Partial<CapabilityOverlap>): CapabilityOverlap => ({
      counted: 1,
      planned: 0,
      isOverlap: false,
      plannedOnTopOfActive: false,
      members: [],
      ...o,
    });

    /** K1.1: overlap og et planlagt oven på. K1.2: ét system. K2.1: kun et planlagt oven på et aktivt. */
    const map = (): CapabilityTreeResponse => ({
      ...capabilityTree([
        capabilityNode('K1', 0, { name: 'Uddannelse' }),
        capabilityNode('K1.1', 1, {
          name: 'Optagelse',
          path: 'Uddannelse',
          selectable: true,
          overlap: overlap({
            counted: 2,
            planned: 1,
            isOverlap: true,
            plannedOnTopOfActive: true,
            members: [
              { id: 'sys-k', name: 'Kompas', exclusion: null },
              { id: 'sys-hr', name: 'Nordlys › HR', exclusion: null },
              { id: 'sys-r', name: 'Rune', exclusion: 'Planlagt' },
              { id: 'sys-u', name: 'Ugle', exclusion: 'Udfases' },
              { id: 'sys-x', name: 'Xylo', exclusion: 'LokalLoesning' },
              { id: 'sys-g', name: 'Gamle', exclusion: 'Nedlagt' },
            ],
          }),
        }),
        capabilityNode('K1.2', 1, {
          name: 'Undervisning',
          path: 'Uddannelse',
          selectable: true,
          overlap: overlap({ members: [{ id: 'sys-k', name: 'Kompas', exclusion: null }] }),
        }),
        capabilityNode('K2', 0, { name: 'Forskning' }),
        capabilityNode('K2.1', 1, {
          name: 'Laboratorier',
          path: 'Forskning',
          selectable: true,
          overlap: overlap({
            planned: 2,
            plannedOnTopOfActive: true,
            members: [
              { id: 'sys-l', name: 'Laborant', exclusion: null },
              { id: 'sys-a', name: 'Alfa', exclusion: 'Planlagt' },
              { id: 'sys-b', name: 'Beta', exclusion: 'Planlagt' },
            ],
          }),
        }),
      ]),
      coverage: { covered: 6, total: 8 },
    });

    const row = (f: ComponentFixture<unknown>, code: string) =>
      Array.from(el(f).querySelectorAll('li')).find((li) => text(li.querySelector('.code')) === code)!;
    const badges = (li: Element) => Array.from(li.querySelectorAll('[data-testid="badge"]')).map((b) => text(b));

    it('dæknings-linjen siger X af Y systemer og moduler og linker til de manglende', async () => {
      const f = await render(map());

      expect(text(q(f, '[data-testid="coverage"]'))).toBe(
        'Kapabiliteter angivet for 6 af 8 systemer og moduler — se de manglende',
      );
      expect(q(f, '[data-testid="missing"]')?.getAttribute('href')).toBe('/systemer?capabilityId=none');
    });

    it('når alle er dækket, er der intet link til de manglende', async () => {
      const f = await render({ ...map(), coverage: { covered: 8, total: 8 } });

      expect(text(q(f, '[data-testid="coverage"]'))).toBe('Kapabiliteter angivet for 8 af 8 systemer og moduler');
      expect(q(f, '[data-testid="missing"]')).toBeNull();
    });

    it('bladene viser serverens mærker og navnene — grupper og blade uden noget at tale om viser intet', async () => {
      const f = await render(map());

      expect(badges(row(f, 'K1.1'))).toEqual(['Overlap · 2 systemer', '1 planlagt system oven på et aktivt']);
      const members = row(f, 'K1.1').querySelector('[data-testid="members"]')!;
      // Alle fire årsager står med deres danske ord.
      expect(text(members)).toBe(
        'Systemer: Kompas, Nordlys › HR, Rune (planlagt), Ugle (udfases), Xylo (lokal løsning/udtræk), Gamle (nedlagt)',
      );
      expect(Array.from(members.querySelectorAll('a')).map((a) => a.getAttribute('href'))).toEqual([
        '/systemer/sys-k',
        '/systemer/sys-hr',
        '/systemer/sys-r',
        '/systemer/sys-u',
        '/systemer/sys-x',
        '/systemer/sys-g',
      ]);
      expect(badges(row(f, 'K2.1'))).toEqual(['2 planlagte systemer oven på et aktivt']);
      for (const code of ['K1', 'K1.2', 'K2']) {
        expect(badges(row(f, code)), code).toEqual([]);
        expect(row(f, code).querySelector('[data-testid="members"]'), code).toBeNull();
      }
      expect(text(q(f, '[data-testid="attention-count"]'))).toBe(
        '1 kapabilitet med overlap · 2 med et planlagt system oven på et aktivt',
      );
    });

    it('"Overlap og planlagte" viser kun dem, der skal tales om, og står i URL\'en', async () => {
      const f = await render(map());
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const pressed = () => q(f, '[data-testid="overlap-filter"]')!.getAttribute('aria-pressed');
      expect(pressed()).toBe('false');

      (q(f, '[data-testid="overlap-filter"]') as HTMLButtonElement).click();
      await settle(f);
      expect(pressed()).toBe('true');

      expect(q(f, '[data-testid="tree"]')).toBeNull();
      const items = Array.from(q(f, '[data-testid="attention"]')!.querySelectorAll(':scope > li'));
      expect(items.map((li) => text(li.querySelector('.code')))).toEqual(['K1.1', 'K2.1']);
      expect(text(items[0])).toContain('Optagelse · Uddannelse');
      expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({ queryParams: { overlap: 1 } }));
      expect(text(q(f, '[data-testid="overlap-filter"]'))).toBe('Vis hele kortet');

      (q(f, '[data-testid="overlap-filter"]') as HTMLButtonElement).click();
      await settle(f);
      expect(q(f, '[data-testid="tree"]')).not.toBeNull();
      expect(pressed()).toBe('false');
      expect(navigate).toHaveBeenLastCalledWith([], expect.objectContaining({ queryParams: { overlap: null } }));
    });

    it('et link med ?overlap=1 åbner direkte i filteret — og siger det, hvis der intet er', async () => {
      await TestBed.inject(Router).navigateByUrl('/?overlap=1');
      const f = await render(capabilityTree([capabilityNode('K1', 0), capabilityNode('K1.1', 1, { selectable: true })]));

      expect(q(f, '[data-testid="tree"]')).toBeNull();
      expect(text(q(f, '[data-testid="no-attention"]'))).toBe(
        'Ingen kapabiliteter har overlap eller et planlagt system oven på et aktivt.',
      );
    });

    it('ordene "løsninger" og "familie" står ikke på kortet', async () => {
      const f = await render(map());
      (q(f, '[data-testid="overlap-filter"]') as HTMLButtonElement).click();
      await settle(f);

      expect(text(el(f))).not.toMatch(/løsninger|famili/i);
    });
  });
});
