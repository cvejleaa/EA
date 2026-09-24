import { Component, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { CapabilityNode, CapabilityOverlap, CapabilityTreeResponse, MoveReason, OverlapMember } from '../api/types';
import { count, moveReasonLabels, overlapExclusionLabels } from '../core/labels';
import { toProblem } from '../core/problem';
import { CapabilitiesApi } from './capabilities.api';

/**
 * Kapabilitetskortet som én udfoldet træliste (så browserens søgning virker). Kortet vedligeholdes KUN ved import.
 * Overlap og dækning er serverens vurdering (docs/plan.md, beslutning I–K) — siden regner intet efter, den viser og
 * filtrerer på serverens flag. Filteret "Overlap og planlagte" står i URL'en (?overlap=1), så det kan linkes.
 */
@Component({
  selector: 'ea-capability-map-page',
  imports: [RouterLink, MatButtonModule, NgTemplateOutlet],
  templateUrl: './capability-map.page.html',
  styleUrl: './capability-map.page.css',
})
export class CapabilityMapPage {
  private readonly api = inject(CapabilitiesApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly tree = signal<CapabilityTreeResponse | null>(null);
  protected readonly overlapOnly = signal(this.route.snapshot.queryParamMap.get('overlap') === '1');

  /** Kapabiliteter, EA skal tale med ejerne om: overlap, eller et planlagt system oven på et aktivt. */
  protected readonly attention = computed(() =>
    (this.tree()?.items ?? []).filter((n) => n.overlap && (n.overlap.isOverlap || n.overlap.plannedOnTopOfActive)),
  );
  protected readonly overlapCount = computed(() => this.attention().filter((n) => n.overlap!.isOverlap).length);
  protected readonly plannedCount = computed(
    () => this.attention().filter((n) => n.overlap!.plannedOnTopOfActive).length,
  );
  protected readonly error = signal<string | null>(null);
  protected readonly downloading = signal(false);
  protected readonly count = count;
  protected reasonLabel(reason: MoveReason | null): string {
    return reason ? moveReasonLabels[reason] : '';
  }

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.tree.set(await this.api.tree());
    } catch (e) {
      this.error.set(`Kortet kunne ikke hentes: ${toProblem(e).message}`);
    }
  }

  protected toggleOverlap(): void {
    const on = !this.overlapOnly();
    this.overlapOnly.set(on);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { overlap: on ? 1 : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /** Mærkerne på en kapabilitet, fx "Overlap · 3 systemer" og "1 planlagt system oven på et aktivt". */
  protected badges(o: CapabilityOverlap | null): string[] {
    if (!o) {
      return [];
    }
    return [
      ...(o.isOverlap ? [`Overlap · ${count(o.counted, 'system', 'systemer')}`] : []),
      ...(o.plannedOnTopOfActive
        ? [`${count(o.planned, 'planlagt system', 'planlagte systemer')} oven på et aktivt`]
        : []),
    ];
  }

  /** Et system med årsagen til, at det ikke tæller, fx "Ugle (udfases)". */
  protected memberText(m: OverlapMember): string {
    return m.exclusion ? `${m.name} (${overlapExclusionLabels[m.exclusion]})` : m.name;
  }

  protected needsAttention(n: CapabilityNode): boolean {
    return this.badges(n.overlap).length > 0;
  }

  protected async download(file: 'model' | 'couplings'): Promise<void> {
    this.downloading.set(true);
    try {
      await (file === 'model' ? this.api.downloadModel() : this.api.downloadCouplings());
    } catch (e) {
      this.error.set(toProblem(e).message);
    } finally {
      this.downloading.set(false);
    }
  }
}
