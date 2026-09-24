import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { LifecycleStatus, SystemListItem, SystemType, TeamDto } from '../api/types';
import { AuthService } from '../core/auth.service';
import { lifecycleLabels, lifecycleOptions, relativeAge, systemTypeLabels, systemTypeOptions } from '../core/labels';
import { toProblem } from '../core/problem';
import { IntegrationsApi } from '../integrations/integrations.api';
import { SystemFilter, SystemsApi } from './systems.api';

const FILTER_KEYS = ['q', 'status', 'type', 'teamId', 'businessOwnerId'] as const;

@Component({
  selector: 'ea-system-list-page',
  imports: [RouterLink, MatTableModule, MatSortModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  templateUrl: './system-list.page.html',
  styleUrl: './system-list.page.css',
})
export class SystemListPage implements OnInit, OnDestroy {
  private readonly api = inject(SystemsApi);
  private readonly integrationsApi = inject(IntegrationsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestNo = 0;

  protected readonly lifecycleLabels = lifecycleLabels;
  protected readonly lifecycleOptions = lifecycleOptions;
  protected readonly typeLabels = systemTypeLabels;
  protected readonly typeOptions = systemTypeOptions;
  protected readonly columns = ['name', 'type', 'status', 'team', 'owner', 'confirmed'];

  protected readonly filter = signal<SystemFilter>({});
  protected readonly items = signal<SystemListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly teams = signal<TeamDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly sort = signal<Sort>({ active: '', direction: '' });
  protected readonly now = signal(new Date());

  protected readonly canCreate = computed(() => this.auth.me()?.permissions.canCreateSystems ?? false);
  protected readonly isFiltered = computed(() => FILTER_KEYS.some((k) => !!this.filter()[k]));

  /** Serverens rækkefølge (moduler under forælderen), medmindre brugeren har sorteret. */
  protected readonly rows = computed(() => {
    const { active, direction } = this.sort();
    const items = this.items();
    if (!direction) {
      return items;
    }
    const factor = direction === 'asc' ? 1 : -1;
    const key = (i: SystemListItem): string =>
      active === 'confirmed' ? i.lastConfirmedAt : active === 'status' ? i.lifecycleStatus : this.displayName(i);
    return [...items].sort((a, b) => key(a).localeCompare(key(b), 'da') * factor);
  });

  async ngOnInit(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    const initial: SystemFilter = {};
    for (const key of FILTER_KEYS) {
      const value = params.get(key);
      if (value) {
        (initial as Record<string, string>)[key] = value;
      }
    }
    this.filter.set(initial);
    try {
      this.teams.set(await this.api.teams());
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
    await this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchTimer);
  }

  protected displayName(item: SystemListItem): string {
    return item.parent ? `${item.parent.name} › ${item.name}` : item.name;
  }

  protected age(iso: string): string {
    return relativeAge(iso, this.now());
  }

  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.setFilter('q', value.trim()), 250);
  }

  protected setFilter(key: keyof SystemFilter, value: string | undefined): void {
    this.filter.update((f) => ({ ...f, [key]: value || undefined }));
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [key]: value || null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    void this.load();
  }

  /** Referencelisten med fulde systemnavne — til den, der udfylder integrations-CSV'en. */
  protected async downloadSystemList(): Promise<void> {
    try {
      await this.integrationsApi.downloadSystemList();
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  protected clearFilters(): void {
    this.filter.set({});
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    void this.load();
  }

  protected asStatus(value: string): LifecycleStatus {
    return value as LifecycleStatus;
  }

  protected asType(value: string): SystemType {
    return value as SystemType;
  }

  private async load(): Promise<void> {
    const requestNo = ++this.requestNo;
    this.loading.set(true);
    try {
      const response = await this.api.list(this.filter());
      if (requestNo !== this.requestNo) {
        return; // Et nyere filter er på vej — vis ikke et forældet svar.
      }
      this.items.set(response.items);
      this.total.set(response.total);
      this.now.set(new Date());
      this.error.set(null);
    } catch (e) {
      if (requestNo === this.requestNo) {
        this.error.set(toProblem(e).message);
      }
    } finally {
      if (requestNo === this.requestNo) {
        this.loading.set(false);
      }
    }
  }
}
