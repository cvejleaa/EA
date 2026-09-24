import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { CouplingImportResult } from '../api/types';
import { provideDanishLocale } from '../core/locale';
import { capabilityTree, couplingResult, couplingSummary, settle, text } from '../testing/fixtures';
import { CouplingImportPage } from './coupling-import.page';

describe('CouplingImportPage', () => {
  let http: HttpTestingController;
  const file = new File(['SystemId;FuldtNavn;Kode\r\n'], 'koblinger.csv', { type: 'text/csv' });

  const kompas = { id: 'sys-k', name: 'Kompas' };
  const hr = { id: 'sys-hr', name: 'Nordlys › HR' };

  /** En tør-kørsel med alt, siden skal kunne vise: fjernes, tilføjes og alle advarsler. */
  const preview = (overrides: Partial<CouplingImportResult> = {}) =>
    couplingResult({
      summary: couplingSummary({
        systemsInFile: 14,
        systemsChanged: 2,
        added: 1,
        removed: 2,
        unchanged: 3,
        systemsCleared: 1,
        largeRemoval: true,
        systemsChangedSinceExport: 1,
        systemsNotInFile: 2,
        ignoredEdits: 1,
      }),
      changes: [
        { kind: 'Fjernes', system: kompas, code: 'K1.1', name: 'Optagelse', path: 'Uddannelse', changedSinceExport: true },
        { kind: 'Fjernes', system: hr, code: 'K1.1', name: 'Optagelse', path: 'Uddannelse', changedSinceExport: false },
        { kind: 'Tilfoejes', system: kompas, code: 'K1.2', name: 'Undervisning', path: '', changedSinceExport: true },
      ],
      notInFile: [
        { id: 'sys-u', name: 'Ugle' },
        { id: 'sys-r', name: 'Rune' },
      ],
      warnings: [
        { line: 4, column: 'Status', message: 'Status er rettet i filen, men indlæses ikke. Ret det på systemsiden.' },
      ],
      fingerprint: 'fp-1',
      ...overrides,
    });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideDanishLocale()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(canImport = true): Promise<ComponentFixture<CouplingImportPage>> {
    const fixture = TestBed.createComponent(CouplingImportPage);
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
  const choose = async (f: ComponentFixture<unknown>) => {
    const input = q(f, '[data-testid="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle(f);
  };
  const cells = (table: Element) =>
    Array.from(table.querySelectorAll('tbody tr')).map((r) => Array.from(r.querySelectorAll('td')).map((c) => text(c)));
  const importRequest = (dryRun: boolean): TestRequest =>
    http.expectOne((r) => r.url === '/api/capabilities/couplings/import' && r.params.get('dryRun') === String(dryRun));

  it('forklarer, at filen bestemmer koblingerne for systemerne i den — og at en slettet række ikke tæller', async () => {
    const f = await render();

    const intro = text(q(f, '.intro'));
    expect(intro).toContain('Filen bestemmer alle koblinger for de systemer og moduler, der står i den.');
    expect(intro).toContain('Systemer, der slet ikke står i filen, røres ikke.');
    expect(intro).toContain('Tøm koden, slet ikke rækken');
    expect(intro).toContain('Kun SystemId og Kode indlæses.');
    // Beslutning N: hverken "løsninger" eller "familie" om et system og dets moduler.
    expect(text(el(f))).not.toMatch(/løsninger|famili/i);
  });

  it('en læser får besked og ingen filvælger', async () => {
    const f = await render(false);

    expect(text(q(f, '[data-testid="not-allowed"]'))).toBe('Kun enterprise arkitekten kan importere koblinger.');
    expect(q(f, '[data-testid="file"]')).toBeNull();
  });

  it('tør-kørslen viser tallene, det der fjernes først, og hver advarsel', async () => {
    const f = await render();
    await choose(f);
    await click(f, 'dry-run');

    const request = importRequest(true);
    expect(request.request.body).toBe(file);
    expect(request.request.headers.get('Content-Type')).toBe('text/csv');
    request.flush(preview());
    await settle(f);

    expect(text(q(f, '[data-testid="summary"]'))).toBe(
      '1 kobling tilføjes · 2 fjernes · 3 uændrede — 2 af filens 14 systemer ændres',
    );
    expect(text(q(f, '[data-testid="large-removal"]'))).toBe(
      'Importen fjerner 2 af de 5 koblinger, systemerne i filen har i dag. Er det en delvis fil?',
    );
    expect(text(q(f, '[data-testid="cleared"]'))).toBe('1 system mister alle sine koblinger.');
    expect(text(q(f, '[data-testid="changed-since-export"]'))).toBe(
      '1 system er ændret i registret, efter filen blev hentet. Tjek, om det, der fjernes, er tilføjet siden — eller hent en ny eksport.',
    );

    const notInFile = q(f, '[data-testid="not-in-file"]')!;
    expect(text(notInFile.querySelector('summary'))).toBe('2 systemer med koblinger står ikke i filen og røres ikke.');
    expect(Array.from(notInFile.querySelectorAll('a')).map((a) => [text(a), a.getAttribute('href')])).toEqual([
      ['Ugle', '/systemer/sys-u'],
      ['Rune', '/systemer/sys-r'],
    ]);

    const warnings = q(f, '[data-testid="warnings"]')!;
    expect(text(warnings.querySelector('summary'))).toBe(
      '1 række har rettelser i kolonner, der ikke indlæses. De gemmes ikke — ret dem på systemsiden.',
    );
    expect(cells(warnings)).toEqual([
      ['4', 'Status', 'Status er rettet i filen, men indlæses ikke. Ret det på systemsiden.'],
    ]);

    const rows = Array.from(q(f, '[data-testid="changes"]')!.querySelectorAll('tbody tr'));
    expect(cells(q(f, '[data-testid="changes"]')!)).toEqual([
      ['Fjernes', 'Kompas Ændret i registret efter eksporten', 'K1.1', 'Optagelse · Uddannelse'],
      ['Fjernes', 'Nordlys › HR', 'K1.1', 'Optagelse · Uddannelse'],
      ['Tilføjes', 'Kompas Ændret i registret efter eksporten', 'K1.2', 'Undervisning'],
    ]);
    expect(rows.map((r) => r.classList.contains('removed'))).toEqual([true, true, false]);
    expect(q(f, '[data-testid="commit"]')).not.toBeNull();
  });

  it('uden advarsler i svaret vises ingen advarsler', async () => {
    const f = await render();
    await choose(f);
    await click(f, 'dry-run');
    importRequest(true).flush(
      preview({
        summary: couplingSummary({ systemsInFile: 2, systemsChanged: 1, added: 1, unchanged: 3 }),
        notInFile: [],
        warnings: [],
      }),
    );
    await settle(f);

    for (const id of ['large-removal', 'cleared', 'changed-since-export', 'not-in-file', 'warnings']) {
      expect(q(f, `[data-testid="${id}"]`), id).toBeNull();
    }
  });

  it('gennemførelsen sender tør-kørslens fingeraftryk og viser resultatet', async () => {
    const f = await render();
    await choose(f);
    await click(f, 'dry-run');
    importRequest(true).flush(preview());
    await settle(f);

    await click(f, 'commit');
    const commit = importRequest(false);
    expect(commit.request.params.get('fingerprint')).toBe('fp-1');
    commit.flush(
      couplingResult({
        committed: true,
        summary: couplingSummary({ systemsInFile: 14, systemsChanged: 2, added: 1, removed: 2, unchanged: 3 }),
      }),
    );
    await settle(f);

    expect(text(q(f, '[data-testid="done"]'))).toBe(
      'Koblingerne er importeret: 1 kobling tilføjet · 2 fjernet · 3 uændrede — 2 af filens 14 systemer ændret. Se kortet',
    );
    expect(q(f, '[data-testid="commit"]')).toBeNull();
  });

  it('fejl i filen vises med linje og kolonne, og der er intet at gennemføre', async () => {
    const f = await render();
    await choose(f);
    await click(f, 'dry-run');
    importRequest(true).flush(
      couplingResult({
        errors: [
          { line: 3, column: 'Kode', message: 'Koden "K9" findes ikke i kortet. Koderne står i "Hent kortet (CSV)".' },
        ],
      }),
    );
    await settle(f);

    expect(cells(q(f, '[data-testid="errors"]')!)).toEqual([
      ['3', 'Kode', 'Koden "K9" findes ikke i kortet. Koderne står i "Hent kortet (CSV)".'],
    ]);
    expect(q(f, '[data-testid="commit"]')).toBeNull();
    expect(q(f, '[data-testid="summary"]')).toBeNull();
  });

  it('en forældet tør-kørsel ved gennemførelse beder om en ny tør-kørsel', async () => {
    const f = await render();
    await choose(f);
    await click(f, 'dry-run');
    importRequest(true).flush(preview());
    await settle(f);
    await click(f, 'commit');
    importRequest(false).flush(
      { type: 'urn:ea:problem:stale-dry-run', detail: 'Koblingerne eller systemerne i filen er ændret …' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(f);

    expect(q(f, '[data-testid="commit"]')).toBeNull();
    await click(f, 'rerun');
    importRequest(true).flush(preview());
    await settle(f);
    expect(q(f, '[data-testid="commit"]')).not.toBeNull();
  });

  it('"Hent koblinger (CSV)" på siden henter eksporten', async () => {
    const f = await render();
    Object.assign(URL, { createObjectURL: vi.fn().mockReturnValue('blob:k'), revokeObjectURL: vi.fn() });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    await click(f, 'download');
    http.expectOne('/api/capabilities/couplings/export.csv').flush(new Blob(['x']));
    await settle(f);

    expect(q(f, '[role="alert"]')).toBeNull();
  });
});
