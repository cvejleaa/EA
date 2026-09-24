import type { LifecycleStatus, SystemRole, SystemType } from '../api/types';

// Danske visningsnavne. Nøglerne er API'ets enum-værdier; `Record` gør, at en ny værdi i kontrakten
// giver en kompileringsfejl her, indtil den har fået et navn.

export const lifecycleLabels: Record<LifecycleStatus, string> = {
  Planlagt: 'Planlagt',
  Indfases: 'Indfases',
  IDrift: 'I drift',
  Udfases: 'Udfases',
  Nedlagt: 'Nedlagt',
};

export const systemTypeLabels: Record<SystemType, string> = {
  Saas: 'SaaS',
  Standardsystem: 'Standardsystem (installeret)',
  Egenudviklet: 'Egenudviklet',
  LowCode: 'Low-code/blanket',
  Platform: 'Platform',
  LokalLoesning: 'Lokal løsning/udtræk',
};

export const roleLabels: Record<SystemRole, string> = {
  Forretningsejer: 'Forretningsejer',
  Systemejer: 'Systemejer',
  Systemforvalter: 'Systemforvalter',
};

export const lifecycleOptions = Object.keys(lifecycleLabels) as LifecycleStatus[];
export const systemTypeOptions = Object.keys(systemTypeLabels) as SystemType[];

const DAY_MS = 24 * 60 * 60 * 1000;

/** "i dag", "for 3 dage siden", "for 2 mdr. siden", "for 1 år siden" — til friskhedssignalet. */
export function relativeAge(iso: string, now: Date): string {
  const days = Math.floor((now.getTime() - new Date(iso).getTime()) / DAY_MS);
  if (days < 1) {
    return 'i dag';
  }
  if (days < 31) {
    return days === 1 ? 'for 1 dag siden' : `for ${days} dage siden`;
  }
  if (days < 365) {
    return `for ${Math.floor(days / 30)} mdr. siden`;
  }
  return `for ${Math.floor(days / 365)} år siden`;
}
