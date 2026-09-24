import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import type { CouplingImportResult, CouplingImportSummary } from '../api/types';
import { ImportFlow } from '../core/import-flow';
import { count, couplingChangeKindLabels, couplingImportSummaryText } from '../core/labels';
import { STALE_DRY_RUN, toProblem } from '../core/problem';
import { CapabilitiesApi } from './capabilities.api';

/**
 * Import af koblings-CSV'en (docs/csv-koblinger.md): filen bestemmer alle koblinger for systemerne i den, og
 * systemer uden for filen røres ikke. Tør-kørslen viser det, der fjernes, først — og advarer, før noget gemmes.
 */
@Component({
  selector: 'ea-coupling-import-page',
  imports: [RouterLink, MatButtonModule],
  templateUrl: './coupling-import.page.html',
  styleUrl: './capability-import.page.css',
})
export class CouplingImportPage {
  private readonly api = inject(CapabilitiesApi);

  protected readonly canImport = signal<boolean | null>(null);
  protected readonly flow = new ImportFlow<CouplingImportResult>((file, dryRun, fingerprint) =>
    this.api.importCouplings(file, dryRun, fingerprint),
  );
  protected readonly downloading = signal(false);

  protected readonly staleDryRun = STALE_DRY_RUN;
  protected readonly kindLabels = couplingChangeKindLabels;
  protected readonly count = count;
  protected readonly summaryText = couplingImportSummaryText;

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.canImport.set((await this.api.tree()).canImport);
    } catch (e) {
      this.flow.problem.set(toProblem(e));
    }
  }

  protected async download(): Promise<void> {
    this.downloading.set(true);
    try {
      await this.api.downloadCouplings();
    } catch (e) {
      this.flow.problem.set(toProblem(e));
    } finally {
      this.downloading.set(false);
    }
  }

  /** Koblingerne på systemerne i filen i dag — det, "fjernes" skal ses i forhold til. */
  protected today(s: CouplingImportSummary): number {
    return s.removed + s.unchanged;
  }

  protected cleared(n: number): string {
    return n === 1 ? '1 system mister alle sine koblinger' : `${n} systemer mister alle deres koblinger`;
  }
}
