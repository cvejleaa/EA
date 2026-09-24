import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import type { IntegrationType, SystemIntegrationItem, SystemIntegrationsResponse, SystemLink } from '../api/types';
import {
  count,
  integrationRelationLabels,
  integrationTypeLabels,
  lifecycleLabels,
  systemDisplayName,
} from '../core/labels';
import { toProblem } from '../core/problem';
import { IntegrationsApi } from './integrations.api';

/**
 * "Hvad hænger på systemet": systemets DIREKTE integrationer (ikke videre led), modulers rullet op.
 * Henter sine egne data, så en fejl her ikke vælter resten af systemsiden.
 */
@Component({
  selector: 'ea-system-integrations',
  imports: [RouterLink, MatButtonModule],
  templateUrl: './system-integrations.component.html',
  styleUrl: './system-integrations.component.css',
})
export class SystemIntegrationsComponent {
  private readonly api = inject(IntegrationsApi);

  readonly systemId = input.required<string>();

  protected readonly data = signal<SystemIntegrationsResponse | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly downloading = signal(false);

  protected readonly relationLabels = integrationRelationLabels;
  protected readonly displayName = systemDisplayName;

  /** Tællelinjen: enheden står ved hvert tal (systemer vs. integrationer blandes aldrig). */
  protected readonly summary = computed(() => {
    const s = this.data()?.summary;
    if (!s || !this.data()?.items.length) {
      return null;
    }
    const parts = [
      `Sender data til ${count(s.receivers, 'system', 'systemer')}`,
      `modtager data fra ${count(s.suppliers, 'system', 'systemer')}`,
    ];
    if (s.localSolutions > 0) {
      parts.push(`heraf ${count(s.localSolutions, 'lokal løsning/udtræk', 'lokale løsninger/udtræk')}`);
    }
    if (s.viaPlatform > 0) {
      parts.push(`${count(s.viaPlatform, 'integration går', 'integrationer går')} via platformen`);
    }
    return parts.join(' · ');
  });

  protected readonly directDbWarning = computed(() => {
    const n = this.data()?.summary.directDb ?? 0;
    return n > 0 ? `${count(n, 'integration', 'integrationer')} med direkte databaseadgang (brud på API First)` : null;
  });

  constructor() {
    effect(() => {
      void this.load(this.systemId());
    });
  }

  /** Det andet system for Ud/Ind; begge ender for Via/Intern. */
  protected otherEnd(item: SystemIntegrationItem): string {
    return item.counterpart
      ? systemDisplayName(item.counterpart)
      : `${systemDisplayName(item.integration.from)} → ${systemDisplayName(item.integration.to)}`;
  }

  protected typeLabel(type: IntegrationType | null): string {
    return type ? integrationTypeLabels[type] : 'Ikke angivet';
  }

  /** Markeringer, der kræver opmærksomhed: livscyklus ≠ I drift og lokale løsninger (skygge-IT). */
  protected flags(system: SystemLink | null): string[] {
    if (!system) {
      return [];
    }
    const flags: string[] = [];
    if (system.lifecycleStatus !== 'IDrift') {
      flags.push(lifecycleLabels[system.lifecycleStatus]);
    }
    if (system.type === 'LokalLoesning') {
      flags.push('Lokal løsning/udtræk');
    }
    return flags;
  }

  protected async download(): Promise<void> {
    this.downloading.set(true);
    try {
      await this.api.downloadCsv(this.systemId());
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.downloading.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    this.data.set(null);
    this.error.set(null);
    try {
      this.data.set(await this.api.forSystem(id));
    } catch (e) {
      this.error.set(`Integrationerne kunne ikke hentes: ${toProblem(e).message}`);
    }
  }
}
