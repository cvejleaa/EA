import { count, importSummaryText, relativeAge, systemDisplayName } from './labels';

describe('relativeAge', () => {
  const now = new Date('2026-09-24T12:00:00Z');
  const daysAgo = (days: number) =>
    new Date(now.getTime() - days * 24 * 60 * 60 * 1000).toISOString();

  it.each([
    [0, 'i dag'],
    [1, 'for 1 dag siden'],
    [2, 'for 2 dage siden'],
    [30, 'for 30 dage siden'],
    [31, 'for 1 mdr. siden'],
    [364, 'for 12 mdr. siden'],
    [365, 'for 1 år siden'],
    [800, 'for 2 år siden'],
  ])('%i dage → %s', (days, expected) => {
    expect(relativeAge(daysAgo(days), now)).toBe(expected);
  });
});

describe('count', () => {
  it.each([
    [0, '0 systemer'],
    [1, '1 system'],
    [2, '2 systemer'],
  ])('%i → %s', (n, expected) => {
    expect(count(n, 'system', 'systemer')).toBe(expected);
  });
});

describe('systemDisplayName', () => {
  it('viser forælder › modul for moduler og ellers navnet', () => {
    expect(systemDisplayName({ name: 'HR', parent: { name: 'Nordlys' } })).toBe('Nordlys › HR');
    expect(systemDisplayName({ name: 'Kompas', parent: null })).toBe('Kompas');
  });
});

describe('importSummaryText', () => {
  it('skriver ental og flertal ved hvert tal og skelner tør-kørsel fra gennemført import', () => {
    const none = {
      retired: 0,
      reactivated: 0,
      removedFromMap: 0,
      couplingsToMove: 0,
      systemsToMove: 0,
    };
    const one = {
      new: 1,
      changed: 1,
      removed: 1,
      unchanged: 1,
      currentTotal: 3,
      largeRemoval: false,
      ...none,
    };
    const many = {
      new: 3,
      changed: 2,
      removed: 4,
      unchanged: 10,
      currentTotal: 16,
      largeRemoval: false,
      ...none,
    };

    expect(importSummaryText(one, false)).toBe('1 ny · 1 ændret · 1 slettes · 1 uændret');
    expect(importSummaryText(many, false)).toBe('3 nye · 2 ændrede · 4 slettes · 10 uændrede');
    expect(importSummaryText(many, true)).toBe('3 nye · 2 ændrede · 4 slettet · 10 uændrede');
  });

  it('nævner udgåede og genaktiverede, kun når der er nogen', () => {
    const s = {
      new: 0,
      changed: 0,
      removed: 1,
      retired: 2,
      reactivated: 1,
      unchanged: 5,
      currentTotal: 8,
      removedFromMap: 3,
      largeRemoval: false,
      couplingsToMove: 3,
      systemsToMove: 2,
    };

    expect(importSummaryText(s, false)).toBe(
      '0 nye · 0 ændrede · 1 slettes · 2 udgår · 1 genaktiveres · 5 uændrede',
    );
    expect(importSummaryText(s, true)).toBe(
      '0 nye · 0 ændrede · 1 slettet · 2 udgået · 1 genaktiveret · 5 uændrede',
    );
  });
});
