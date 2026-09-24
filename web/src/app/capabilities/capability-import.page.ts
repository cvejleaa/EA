import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import type { CapabilityChange, CapabilityImportResult, CapabilitySnapshot } from '../api/types';
import { capabilityChangeKindLabels, count, importSummaryText } from '../core/labels';
import { STALE_DRY_RUN, toProblem, type ProblemInfo } from '../core/problem';
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
  protected readonly file = signal<File | null>(null);
  protected readonly result = signal<CapabilityImportResult | null>(null);
  protected readonly done = signal<CapabilityImportResult | null>(null);
  protected readonly problem = signal<ProblemInfo | null>(null);
  protected readonly busy = signal(false);

  protected readonly staleDryRun = STALE_DRY_RUN;
  protected readonly kindLabels = capabilityChangeKindLabels;
  protected readonly count = count;
  protected readonly summaryText = importSummaryText;

  protected readonly canCommit = computed(() => {
    const r = this.result();
    return !!r && r.errors.length === 0 && r.changes.length > 0 && !!r.fingerprint;
  });

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
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
    this.result.set(null);
    this.done.set(null);
    this.problem.set(null);
  }

  protected async dryRun(): Promise<void> {
    const file = this.file();
    if (!file) {
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    this.done.set(null);
    try {
      this.result.set(await this.api.import(file, true));
    } catch (e) {
      this.result.set(null);
      this.problem.set(toProblem(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async commit(): Promise<void> {
    const file = this.file();
    const fingerprint = this.result()?.fingerprint;
    if (!file || !fingerprint) {
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      this.done.set(await this.api.import(file, false, fingerprint));
      this.result.set(null);
    } catch (e) {
      // Uanset årsag gælder tør-kørslen ikke længere: knappen skjules, til en ny tør-kørsel er lavet.
      this.result.set(null);
      this.problem.set(toProblem(e));
    } finally {
      this.busy.set(false);
    }
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
