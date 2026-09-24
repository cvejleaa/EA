import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import type { CapabilityTreeResponse, MoveReason } from '../api/types';
import { count, moveReasonLabels } from '../core/labels';
import { toProblem } from '../core/problem';
import { CapabilitiesApi } from './capabilities.api';

/**
 * Kapabilitetskortet som én udfoldet træliste (så browserens søgning virker). Kortet vedligeholdes KUN ved
 * import; koblinger til systemer og overlap kommer i de næste skiver (docs/plan.md, delopgave 3).
 */
@Component({
  selector: 'ea-capability-map-page',
  imports: [RouterLink, MatButtonModule],
  templateUrl: './capability-map.page.html',
  styleUrl: './capability-map.page.css',
})
export class CapabilityMapPage {
  private readonly api = inject(CapabilitiesApi);

  protected readonly tree = signal<CapabilityTreeResponse | null>(null);
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
