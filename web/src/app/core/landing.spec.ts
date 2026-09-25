import { me } from '../testing/fixtures';
import { MINE_URL, landingUrl } from './landing';

describe('landingUrl (hvor man lander efter login)', () => {
  const steward = me(false, { mySystemCount: 3 });

  it('et bestemt mål vinder', () => {
    expect(landingUrl(steward, '/systemer/sys-1')).toBe('/systemer/sys-1');
    expect(landingUrl(steward, '/kapabiliteter?overlap=1')).toBe('/kapabiliteter?overlap=1');
  });

  it('standardadressen er ikke et mål — forvalteren med systemer lander på "Mine systemer"', () => {
    // Vagten sætter altid returnUrl=/systemer, også når man bare åbnede forsiden (QC, 4b-1).
    expect(landingUrl(steward, '/systemer')).toBe(MINE_URL);
    expect(landingUrl(steward, null)).toBe(MINE_URL);
    expect(MINE_URL).toBe('/systemer?mine=true');
  });

  it('enterprise arkitekten og den uden roller lander på alle systemer', () => {
    expect(landingUrl(me(true, { mySystemCount: 3 }), '/systemer')).toBe('/systemer');
    expect(landingUrl(me(false, { mySystemCount: 0 }), null)).toBe('/systemer');
  });

  it('et mål uden for appen ignoreres', () => {
    expect(landingUrl(steward, '//ondt.example/x')).toBe(MINE_URL);
    expect(landingUrl(steward, 'https://ondt.example/x')).toBe(MINE_URL);
  });
});
