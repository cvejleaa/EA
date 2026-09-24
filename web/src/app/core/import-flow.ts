import { computed, signal } from '@angular/core';
import type { ImportRowError } from '../api/types';
import { toProblem, type ProblemInfo } from './problem';

/** Det, en tør-kørsel eller import svarer — fælles for kortet og koblingerne. */
export interface ImportResultLike {
  errors: ImportRowError[];
  changes: unknown[];
  fingerprint?: string | null;
}

/**
 * Tilstanden på en importside: vælg fil → tør-kørsel (intet gemmes) → gennemfør med tør-kørslens fingeraftryk.
 * "Gennemfør" findes kun efter en fejlfri tør-kørsel af netop den valgte fil, med noget at ændre. Samme logik for
 * kortet og koblingerne; siderne har hver deres tekster.
 */
export class ImportFlow<T extends ImportResultLike> {
  readonly file = signal<File | null>(null);
  readonly result = signal<T | null>(null);
  readonly done = signal<T | null>(null);
  readonly problem = signal<ProblemInfo | null>(null);
  readonly busy = signal(false);

  readonly canCommit = computed(() => {
    const r = this.result();
    return !!r && r.errors.length === 0 && r.changes.length > 0 && !!r.fingerprint;
  });

  constructor(private readonly send: (file: Blob, dryRun: boolean, fingerprint?: string) => Promise<T>) {}

  /** En ny fil: tør-kørslen og beskederne gjaldt den gamle. */
  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
    this.result.set(null);
    this.done.set(null);
    this.problem.set(null);
  }

  async dryRun(): Promise<void> {
    const file = this.file();
    if (!file) {
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    this.done.set(null);
    try {
      this.result.set(await this.send(file, true));
    } catch (e) {
      this.result.set(null);
      this.problem.set(toProblem(e));
    } finally {
      this.busy.set(false);
    }
  }

  async commit(): Promise<void> {
    const file = this.file();
    const fingerprint = this.result()?.fingerprint;
    if (!file || !fingerprint) {
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      this.done.set(await this.send(file, false, fingerprint));
      this.result.set(null);
    } catch (e) {
      // Uanset årsag gælder tør-kørslen ikke længere: knappen skjules, til en ny tør-kørsel er lavet.
      this.result.set(null);
      this.problem.set(toProblem(e));
    } finally {
      this.busy.set(false);
    }
  }
}
