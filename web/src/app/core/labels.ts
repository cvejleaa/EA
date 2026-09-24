import type {
  CapabilityChangeKind,
  CapabilityImportSummary,
  IntegrationRelation,
  IntegrationType,
  LifecycleStatus,
  SystemRole,
  SystemType,
} from '../api/types';

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

/** Integrationstyper. DirekteDb er et brud på arkitekturprincip 3 (API First). Spejles af docs/csv-integrationer.md. */
export const integrationTypeLabels: Record<IntegrationType, string> = {
  Api: 'API',
  Fil: 'Fil',
  Event: 'Event/besked',
  DirekteDb: 'Direkte databaseadgang',
  Udtraek: 'Udtræk',
};

/** Det viste systems forhold til en integration — retning er dataflow. */
export const integrationRelationLabels: Record<IntegrationRelation, string> = {
  Ud: 'Sender data til',
  Via: 'Går via platformen',
  Ind: 'Modtager data fra',
  Intern: 'Internt',
};

/** Hvad en import gør ved én kapabilitet (tør-kørslens ændringsliste). */
export const capabilityChangeKindLabels: Record<CapabilityChangeKind, string> = {
  Ny: 'Ny',
  Aendret: 'Ændret',
  Slettes: 'Slettes',
};

export const lifecycleOptions = Object.keys(lifecycleLabels) as LifecycleStatus[];
export const integrationTypeOptions = Object.keys(integrationTypeLabels) as IntegrationType[];

/** "1 system" / "4 systemer" — tal og enhed hører sammen, så tællinger aldrig blander enheder. */
export function count(n: number, singular: string, plural: string): string {
  return `${n} ${n === 1 ? singular : plural}`;
}

/** "Forælder › Modul" for et modul, ellers bare navnet. */
/** Tør-kørslens (eller importens) tal i ét udsagn, fx "3 nye · 1 ændret · 2 slettes · 10 uændrede". */
export function importSummaryText(s: CapabilityImportSummary, committed: boolean): string {
  return [
    count(s.new, 'ny', 'nye'),
    count(s.changed, 'ændret', 'ændrede'),
    `${s.removed} ${committed ? 'slettet' : 'slettes'}`,
    count(s.unchanged, 'uændret', 'uændrede'),
  ].join(' · ');
}

export function systemDisplayName(system: { name: string; parent: { name: string } | null }): string {
  return system.parent ? `${system.parent.name} › ${system.name}` : system.name;
}
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
