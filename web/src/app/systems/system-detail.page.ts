import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterLink } from '@angular/router';
import type { LifecycleStatus, OverlapMember, PersonDto, SystemDetail, SystemRole, SystemType } from '../api/types';
import {
  lifecycleLabels,
  moveReasonLabels,
  overlapExclusionLabels,
  relativeAge,
  roleLabels,
  systemTypeLabels,
} from '../core/labels';
import { toProblem } from '../core/problem';
import { SystemIntegrationsComponent } from '../integrations/system-integrations.component';
import { SystemsApi } from './systems.api';

@Component({
  selector: 'ea-system-detail-page',
  imports: [RouterLink, DatePipe, NgTemplateOutlet, MatButtonModule, SystemIntegrationsComponent],
  templateUrl: './system-detail.page.html',
  styleUrl: './system-detail.page.css',
})
export class SystemDetailPage {
  private readonly api = inject(SystemsApi);
  private readonly router = inject(Router);

  /** Route-parameteren :id. */
  readonly id = input.required<string>();

  protected readonly system = signal<SystemDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly confirmingDelete = signal(false);
  protected readonly busy = signal(false);
  protected readonly now = signal(new Date());

  protected readonly roleLabels = roleLabels;
  protected readonly moveReasonLabels = moveReasonLabels;
  protected readonly exclusionLabels = overlapExclusionLabels;

  /** Et system med årsagen til, at det ikke tæller med i overlap, fx "Ugle (udfases)". */
  protected memberText(m: OverlapMember): string {
    return m.exclusion ? `${m.name} (${overlapExclusionLabels[m.exclusion]})` : m.name;
  }

  protected hasShared(s: SystemDetail): boolean {
    return s.capabilities.some((c) => c.sharedWith.length > 0);
  }

  protected readonly businessOwner = computed(() => this.holders('Forretningsejer')[0] ?? null);
  protected readonly systemOwner = computed(() => this.holders('Systemejer')[0] ?? null);
  protected readonly stewards = computed(() => this.holders('Systemforvalter'));

  constructor() {
    effect(() => {
      void this.load(this.id());
    });
  }

  protected statusLabel(status: LifecycleStatus): string {
    return lifecycleLabels[status];
  }

  protected typeLabel(type: SystemType | null): string {
    return type ? systemTypeLabels[type] : 'Ikke angivet';
  }

  protected age(iso: string): string {
    return relativeAge(iso, this.now());
  }

  protected async confirmUnchanged(): Promise<void> {
    const s = this.system();
    if (!s) {
      return;
    }
    await this.run(async () => this.system.set(await this.api.confirm(s.id, s.version)));
  }

  protected async delete(): Promise<void> {
    const s = this.system();
    if (!s) {
      return;
    }
    await this.run(async () => {
      await this.api.delete(s.id);
      await this.router.navigate(['/systemer']);
    });
  }

  private holders(role: SystemRole): PersonDto[] {
    return (this.system()?.roles ?? []).filter((r) => r.role === role).map((r) => r.person);
  }

  private async load(id: string): Promise<void> {
    this.system.set(null);
    this.error.set(null);
    this.actionError.set(null);
    this.confirmingDelete.set(false);
    try {
      this.system.set(await this.api.get(id));
      this.now.set(new Date());
    } catch (e) {
      const problem = toProblem(e);
      this.error.set(problem.status === 404 ? 'Systemet findes ikke (længere).' : problem.message);
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await action();
      this.now.set(new Date());
    } catch (e) {
      this.actionError.set(toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
