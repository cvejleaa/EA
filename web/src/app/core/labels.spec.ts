import { relativeAge } from './labels';

describe('relativeAge', () => {
  const now = new Date('2026-09-24T12:00:00Z');
  const daysAgo = (days: number) => new Date(now.getTime() - days * 24 * 60 * 60 * 1000).toISOString();

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
