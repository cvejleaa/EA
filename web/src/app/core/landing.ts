import type { MeResponse } from '../api/types';

/** Adressen for "Mine systemer" — samme stavemåde som API'ets parameter. */
export const MINE_URL = '/systemer?mine=true';

/**
 * Hvor man lander efter login. Et bestemt mål (returnUrl) vinder — men standardadressen /systemer er ikke et mål:
 * vagten sætter altid returnUrl, også når man bare åbnede forsiden. Så lander den, der ikke er enterprise arkitekt og
 * har en rolle på systemer, på "Mine systemer" (QC, 4b-1). Bruges af alle login-flows (dev-login nu, e-mail-link i F2).
 */
export function landingUrl(me: MeResponse, returnUrl: string | null): string {
  const target = returnUrl?.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : null;
  if (target && target !== '/systemer') {
    return target;
  }
  return !me.permissions.canCreateSystems && me.mySystemCount > 0 ? MINE_URL : '/systemer';
}
