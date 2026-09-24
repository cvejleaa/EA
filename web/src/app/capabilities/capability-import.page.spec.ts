import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { CapabilityImportResult } from '../api/types';
import { provideDanishLocale } from '../core/locale';
import { capabilityTree, importResult, importSummary, settle, text } from '../testing/fixtures';
import { CapabilityImportPage } from './capability-import.page';

describe('CapabilityImportPage', () => {
  let http: HttpTestingController;
  const file = new File(['Kode;Navn;ForælderKode;Beskrivelse\r\nK1;En;;\r\n'], 'kort.csv', {
    type: 'text/csv',
  });

  const preview = (overrides: Partial<CapabilityImportResult> = {}) =>
    importResult({
      summary: importSummary({ new: 1, changed: 2, removed: 1, unchanged: 4, currentTotal: 8 }),
      changes: [
        {
          kind: 'Slettes',
          code: 'K3',
          before: { code: 'K3', name: 'Gammel', parentCode: null, description: null },
          after: null,
          affectedSystems: [],
        },
        {
          kind: 'Aendret',
          code: 'K1.1',
          before: { code: 'K1.1', name: 'Studier', parentCode: 'K1', description: 'a' },
          after: { code: 'K1.1', name: 'Studieadministration', parentCode: 'K1', description: 'b' },
          affectedSystems: [],
        },
        {
          kind: 'Aendret',
          code: 'K2',
          before: { code: 'K2', name: 'Forsk', parentCode: null, description: 'samme' },
          after: { code: 'K2', name: 'Forskning', parentCode: null, description: 'samme' },
          affectedSystems: [],
        },
        {
          kind: 'Ny',
          code: 'K4',
          before: null,
          after: { code: 'K4', name: 'Ny ting', parentCode: null, description: null },
          affectedSystems: [],
        },
      ],
      fingerprint: 'abc123',
      ...overrides,
    });

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

  async function render(canImport = true): Promise<ComponentFixture<CapabilityImportPage>> {
    const fixture = TestBed.createComponent(CapabilityImportPage);
    fixture.detectChanges();
    http.expectOne('/api/capabilities').flush(capabilityTree([], canImport));
    await settle(fixture);
    return fixture;
  }

  const el = (f: ComponentFixture<unknown>) => f.nativeElement as HTMLElement;
  const q = (f: ComponentFixture<unknown>, selector: string) => el(f).querySelector(selector);
  const click = async (f: ComponentFixture<unknown>, testId: string) => {
    (q(f, `[data-testid="${testId}"]`) as HTMLButtonElement).click();
    await settle(f);
  };
  const choose = async (f: ComponentFixture<unknown>, chosen: File) => {
    const input = q(f, '[data-testid="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [chosen], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle(f);
  };
  const importRequest = (): TestRequest =>
    http.expectOne((r) => r.url === '/api/capabilities/import');

  it('tør-kørslen sender filen som text/csv og gemmer intet', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');

    const request = importRequest();
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe(file);
    expect(request.request.headers.get('Content-Type')).toBe('text/csv');
    expect(request.request.params.get('dryRun')).toBe('true');
    expect(request.request.params.has('fingerprint')).toBe(false);
    request.flush(preview());
    await settle(f);

    expect(text(q(f, '[data-testid="summary"]'))).toBe('1 ny · 2 ændrede · 1 slettes · 4 uændrede');
    const rows = Array.from(el(f).querySelectorAll('[data-testid="changes"] tbody tr')).map((tr) =>
      Array.from(tr.querySelectorAll('td')).map((td) => text(td)),
    );
    expect(rows).toEqual([
      ['Slettes', 'K3', 'Gammel (øverste niveau)', '—'],
      [
        'Ændret',
        'K1.1',
        'Studier (under K1)',
        'Studieadministration (under K1) · beskrivelse ændret',
      ],
      ['Ændret', 'K2', 'Forsk (øverste niveau)', 'Forskning (øverste niveau)'], // Samme beskrivelse: ingen markering.
      ['Ny', 'K4', '—', 'Ny ting (øverste niveau)'],
    ]);
    // Kun det, der slettes, er markeret (rødt).
    expect(
      Array.from(el(f).querySelectorAll('[data-testid="changes"] tbody tr')).map((tr) =>
        tr.classList.contains('removed'),
      ),
    ).toEqual([true, false, false, false]);
    expect(q(f, '[data-testid="large-removal"]')).toBeNull();
    expect(q(f, '[data-testid="commit"]')).not.toBeNull();
  });

  it('gennemførelsen sender tør-kørslens fingeraftryk og viser resultatet', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview());
    await settle(f);

    await click(f, 'commit');
    const request = importRequest();
    expect(request.request.params.get('dryRun')).toBe('false');
    expect(request.request.params.get('fingerprint')).toBe('abc123');
    expect(request.request.body).toBe(file);
    request.flush(preview({ committed: true, fingerprint: null }));
    await settle(f);

    expect(text(q(f, '[data-testid="done"]'))).toBe(
      'Kortet er importeret: 1 ny · 2 ændrede · 1 slettet · 4 uændrede. Se kortet',
    );
    expect(q(f, '[data-testid="commit"]')).toBeNull();
  });

  it('fejl i filen vises med linjenummer, og der er intet at gennemføre', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(
      importResult({
        errors: [
          { line: 3, column: 'Kode', message: 'Koden "k1" står også i linje 2.' },
          { line: 7, column: null, message: 'Filen er ikke gemt som UTF-8.' },
        ],
      }),
    );
    await settle(f);

    const errors = q(f, '[data-testid="errors"]');
    expect(text(errors?.querySelector('p'))).toBe(
      'Filen har 2 fejl. Ret dem i regnearket, og kør tør-kørslen igen. Intet er gemt.',
    );
    expect(
      Array.from(errors!.querySelectorAll('tbody tr')).map((tr) =>
        Array.from(tr.querySelectorAll('td')).map((td) => text(td)),
      ),
    ).toEqual([
      ['3', 'Kode', 'Koden "k1" står også i linje 2.'],
      ['7', '', 'Filen er ikke gemt som UTF-8.'],
    ]);
    expect(q(f, '[data-testid="commit"]')).toBeNull();
    expect(q(f, '[data-testid="summary"]')).toBeNull();
  });

  it('en stor sletning giver en tydelig advarsel', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(
      preview({
        summary: importSummary({
          removed: 40,
          unchanged: 60,
          currentTotal: 100,
          largeRemoval: true,
        }),
      }),
    );
    await settle(f);

    expect(text(q(f, '[data-testid="large-removal"]'))).toBe(
      'Importen fjerner 40 af kortets 100 kapabiliteter. Filen skal være hele kortet — er det en delvis fil?',
    );
  });

  it('det, der udgår, vises med de systemer, hvis koblinger bør flyttes', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(
      preview({
        summary: importSummary({
          retired: 1,
          unchanged: 5,
          currentTotal: 6,
          couplingsToMove: 2,
          systemsToMove: 2,
        }),
        changes: [
          {
            kind: 'Udgaar',
            code: 'K2.1',
            before: { code: 'K2.1', name: 'Laboratorier', parentCode: 'K2', description: null },
            after: null,
            affectedSystems: [
              { id: 's1', name: 'Laborant' },
              { id: 's2', name: 'Nordlys › HR' },
            ],
          },
          {
            kind: 'Ny',
            code: 'K9',
            before: null,
            after: { code: 'K9', name: 'Ny', parentCode: null, description: null },
            affectedSystems: [],
          },
        ],
      }),
    );
    await settle(f);

    expect(text(q(f, '[data-testid="summary"]'))).toBe(
      '0 nye · 0 ændrede · 0 slettes · 1 udgår · 5 uændrede',
    );
    expect(text(q(f, '[data-testid="to-move"]'))).toBe(
      '2 koblinger på 2 systemer bør flyttes bagefter: de peger på en kapabilitet, der udgår eller får underkapabiliteter. Koblingerne bevares, indtil de er flyttet.',
    );
    const rows = Array.from(el(f).querySelectorAll('[data-testid="changes"] tbody tr'));
    expect(text(rows[0].querySelector('td'))).toBe('Udgår');
    expect(text(rows[0].querySelector('[data-testid="affected"]'))).toBe(
      'Koblinger bør flyttes: Laborant, Nordlys › HR',
    );
    expect(rows[1].querySelector('[data-testid="affected"]')).toBeNull();
    expect(rows.map((tr) => tr.classList.contains('removed'))).toEqual([true, false]);
  });

  it('uden koblinger at flytte er der ingen besked om det', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview());
    await settle(f);

    expect(q(f, '[data-testid="summary"]')).not.toBeNull();
    expect(q(f, '[data-testid="to-move"]')).toBeNull();
  });

  it('en fil uden ændringer har intet at gennemføre', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(
      importResult({ summary: importSummary({ unchanged: 7, currentTotal: 7 }), fingerprint: 'x' }),
    );
    await settle(f);

    expect(text(q(f, '[data-testid="no-changes"]'))).toBe(
      'Filen er identisk med kortet. Der er intet at importere.',
    );
    expect(q(f, '[data-testid="commit"]')).toBeNull();
  });

  it('vælges en ny fil, forsvinder knappen — tør-kørslen gjaldt den gamle', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview());
    await settle(f);
    expect(q(f, '[data-testid="commit"]')).not.toBeNull();

    await choose(f, new File(['andet'], 'andet.csv'));

    expect(q(f, '[data-testid="commit"]')).toBeNull();
    expect(q(f, '[data-testid="summary"]')).toBeNull();
  });

  it('en ny fil efter en gennemført import fjerner beskeden om den gamle', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview());
    await settle(f);
    await click(f, 'commit');
    importRequest().flush(preview({ committed: true, fingerprint: null }));
    await settle(f);
    expect(q(f, '[data-testid="done"]')).not.toBeNull();

    await choose(f, new File(['andet'], 'andet.csv'));

    expect(q(f, '[data-testid="done"]')).toBeNull();
  });

  it('en ny fil efter en fejl fjerner fejlen og "Kør tør-kørsel igen"', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview());
    await settle(f);
    await click(f, 'commit');
    importRequest().flush(
      { type: 'urn:ea:problem:stale-dry-run', detail: 'Kortet er ændret.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(f);
    expect(q(f, '[data-testid="problem"]')).not.toBeNull();

    await choose(f, new File(['andet'], 'andet.csv'));

    expect(q(f, '[data-testid="problem"]')).toBeNull();
    expect(q(f, '[data-testid="rerun"]')).toBeNull();
  });

  it('knapperne er spærret, mens en tør-kørsel eller import er i gang', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    const dryRun = importRequest();
    expect((q(f, '[data-testid="dry-run"]') as HTMLButtonElement).disabled).toBe(true);
    dryRun.flush(preview());
    await settle(f);
    expect((q(f, '[data-testid="dry-run"]') as HTMLButtonElement).disabled).toBe(false);

    await click(f, 'commit');
    const commit = importRequest();
    expect((q(f, '[data-testid="commit"]') as HTMLButtonElement).disabled).toBe(true);
    commit.flush(preview({ committed: true, fingerprint: null }));
    await settle(f);
  });

  it('uden fingeraftryk fra serveren er der intet at gennemføre', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview({ fingerprint: null }));
    await settle(f);

    expect(q(f, '[data-testid="changes"]')).not.toBeNull();
    expect(q(f, '[data-testid="commit"]')).toBeNull();
  });

  it.each([
    ['urn:ea:problem:stale-dry-run', true],
    ['urn:ea:problem:stale-version', false],
  ])('konflikt %s ved gennemførelse: "Kør tør-kørsel igen" vises: %s', async (type, showsRerun) => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview());
    await settle(f);

    await click(f, 'commit');
    importRequest().flush(
      { type, detail: 'Kortet er ændret.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(f);

    expect(text(q(f, '[data-testid="problem"] p'))).toBe('Kortet er ændret.');
    expect(!!q(f, '[data-testid="rerun"]')).toBe(showsRerun);
    expect(q(f, '[data-testid="commit"]')).toBeNull();
  });

  it('kør tør-kørsel igen efter en forældet tør-kørsel sender en ny tør-kørsel', async () => {
    const f = await render();
    await choose(f, file);
    await click(f, 'dry-run');
    importRequest().flush(preview());
    await settle(f);
    await click(f, 'commit');
    importRequest().flush(
      { type: 'urn:ea:problem:stale-dry-run', detail: 'Kortet er ændret.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(f);

    await click(f, 'rerun');

    expect(importRequest().request.params.get('dryRun')).toBe('true');
  });

  it('en læser får besked og ingen filvælger', async () => {
    const f = await render(false);

    expect(text(q(f, '[data-testid="not-allowed"]'))).toBe(
      'Kun enterprise arkitekten kan importere kortet.',
    );
    expect(q(f, '[data-testid="file"]')).toBeNull();
    expect(q(f, '[data-testid="dry-run"]')).toBeNull();
  });

  it('tør-kørsel kræver en valgt fil', async () => {
    const f = await render();

    expect((q(f, '[data-testid="dry-run"]') as HTMLButtonElement).disabled).toBe(true);
    await choose(f, file);
    expect((q(f, '[data-testid="dry-run"]') as HTMLButtonElement).disabled).toBe(false);
  });
});
