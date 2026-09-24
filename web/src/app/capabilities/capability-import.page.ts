import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import type { CapabilityChange, CapabilityImportResult, CapabilitySnapshot } from '../api/types';
import { capabilityChangeKindLabels, count, importSummaryText } from '../core/labels';
import { ImportFlow } from '../core/import-flow';
import { STALE_DRY_RUN, toProblem } from '../core/problem';
import { CapabilitiesApi } from './capabilities.api';

/**
 * Import af HELE kapabilitetskortet: vælg fil → tør-kørsel (før/efter, intet gemmes) → gennemfør. Knappen
 * "Gennemfør import" findes kun efter en fejlfri tør-kørsel af netop den valgte fil, med noget at ændre.
 */
@Component({
  selector: 'ea-capability-import-page',
  imports: [RouterLink, MatButtonModule],
  templateUrl: './capability-import.page.html',
  styleUrl: './capability-import.page.css',
})
export class CapabilityImportPage {
  private readonly api = inject(CapabilitiesApi);

  protected readonly canImport = signal<boolean | null>(null);
  private readonly flow = new ImportFlow<CapabilityImportResult>((file, dryRun, fingerprint) =>
    this.api.import(file, dryRun, fingerprint),
  );
  protected readonly file = this.flow.file;
  protected readonly result = this.flow.result;
  protected readonly done = this.flow.done;
  protected readonly problem = this.flow.problem;
  protected readonly busy = this.flow.busy;
  protected readonly canCommit = this.flow.canCommit;

  protected readonly staleDryRun = STALE_DRY_RUN;
  protected readonly kindLabels = capabilityChangeKindLabels;
  protected readonly count = count;
  protected readonly summaryText = importSummaryText;

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.canImport.set((await this.api.tree()).canImport);
    } catch (e) {
      this.problem.set(toProblem(e));
    }
  }

  protected onFile(event: Event): void {
    this.flow.onFile(event);
  }

  protected dryRun(): Promise<void> {
    return this.flow.dryRun();
  }

  protected commit(): Promise<void> {
    return this.flow.commit();
  }

  protected describe(s: CapabilitySnapshot | null | undefined): string {
    if (!s) {
      return '—';
    }
    return `${s.name} (${s.parentCode ? `under ${s.parentCode}` : 'øverste niveau'})`;
  }

  protected systemNames(c: CapabilityChange): string {
    return c.affectedSystems.map((s) => s.name).join(', ');
  }

  protected descriptionChanged(before?: CapabilitySnapshot | null, after?: CapabilitySnapshot | null): boolean {
    return !!before && !!after && (before.description ?? null) !== (after.description ?? null);
  }
}
