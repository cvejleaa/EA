import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import type { Subscription } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import type { CapabilityNode, LifecycleStatus, SystemListItem, SystemRole, SystemType, TeamDto } from '../api/types';
import { AuthService } from '../core/auth.service';
import { lifecycleLabels, lifecycleOptions, relativeAge, roleLabels, systemTypeLabels, systemTypeOptions } from '../core/labels';
import { toProblem } from '../core/problem';
import { CapabilitiesApi } from '../capabilities/capabilities.api';
import { IntegrationsApi } from '../integrations/integrations.api';
import { SystemFilter, SystemsApi } from './systems.api';

const FILTER_KEYS = ['q', 'status', 'type', 'teamId', 'businessOwnerId', 'capabilityId', 'mine'] as const;

function filterFrom(params: ParamMap): SystemFilter {
  const filter: SystemFilter = {};
  for (const key of FILTER_KEYS) {
    const value = params.get(key);
    if (value) {
      (filter as Record<string, string>)[key] = value;
    }
  }
  return filter;
}

const sameFilter = (a: SystemFilter, b: SystemFilter): boolean => FILTER_KEYS.every((k) => (a[k] ?? '') === (b[k] ?? ''));

@Component({
  selector: 'ea-system-list-page',
  imports: [RouterLink, MatTableModule, MatSortModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  templateUrl: './system-list.page.html',
  styleUrl: './system-list.page.css',
})
export class SystemListPage implements OnInit, OnDestroy {
  private readonly api = inject(SystemsApi);
  private readonly integrationsApi = inject(IntegrationsApi);
  private readonly capabilitiesApi = inject(CapabilitiesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestNo = 0;
  private params?: Subscription;

  protected readonly lifecycleLabels = lifecycleLabels;
  protected readonly lifecycleOptions = lifecycleOptions;
  protected readonly typeLabels = systemTypeLabels;
  protected readonly typeOptions = systemTypeOptions;
  protected readonly roleLabels = roleLabels;

  protected readonly filter = signal<SystemFilter>({});
  protected readonly items = signal<SystemListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly teams = signal<TeamDto[]>([]);
  /** Kapabiliteten, listen er filtreret på (når filteret er et id — fx fra kortet). */
  protected readonly capability = signal<Pick<CapabilityNode, 'code' | 'name'> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly sort = signal<Sort>({ active: '', direction: '' });
  protected readonly now = signal(new Date());

  protected readonly canCreate = computed(() => this.auth.me()?.permissions.canCreateSystems ?? false);
  protected readonly mine = computed(() => this.filter().mine === 'true');
  /** Er "mine" det eneste filter? Så betyder en tom liste, at brugeren ingen roller har — ikke at intet matcher. */
  protected readonly onlyMine = computed(() => FILTER_KEYS.every((k) => k === 'mine' || !this.filter()[k]));
  /** Knappen vises for den, der har mine systemer — og altid, mens filtret er slået til (så det kan slås fra). */
  protected readonly showMineToggle = computed(() => (this.auth.me()?.mySystemCount ?? 0) > 0 || this.mine());
  /** "Din rolle" kun i "Mine systemer": hvilke af dem er MIT ansvar (domæne-rådgiveren). */
  protected readonly columns = computed(() =>
    this.mine()
      ? ['name', 'myRole', 'type', 'status', 'team', 'owner', 'confirmed']
      : ['name', 'type', 'status', 'team', 'owner', 'confirmed'],
  );
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
    try {
      this.teams.set(await this.api.teams());
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
    // URL'en er sandheden: et link til samme side (fx "Mine systemer" i menuen) genbruger komponenten, så listen
    // følger query-parametrene — men henter ikke igen, når den selv har skrevet dem (setFilter).
    let first = true;
    this.params = this.route.queryParamMap.subscribe((params) => {
      const next = filterFrom(params);
      if (!first && sameFilter(next, this.filter())) {
        return;
      }
      first = false;
      this.filter.set(next);
      this.capability.set(null);
      void this.load();
      void this.loadCapabilityName(next.capabilityId);
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchTimer);
    this.params?.unsubscribe();
  }

  /** "Din rolle": brugerens egne roller — eller forælderen, når systemet kun er med som modul under et af hendes. */
  protected myRole(item: SystemListItem): string {
    const roles = (item.myRoles ?? []) as SystemRole[];
    return roles.length ? roles.map((r) => this.roleLabels[r]).join(', ') : `Via ${item.parent?.name ?? 'forælderen'}`;
  }

  /**
   * Navnet på kapabiliteten i filteret (fx fra kortet eller systemsiden). En udgået kapabilitet står ikke i
   * træet, men på listen over koblinger, der bør flyttes. Kan navnet ikke hentes, filtreres der alligevel.
   */
  private async loadCapabilityName(capabilityId: string | undefined): Promise<void> {
    if (!capabilityId || capabilityId === 'none') {
      return;
    }
    try {
      const tree = await this.capabilitiesApi.tree();
      this.capability.set(
        tree.items.find((c) => c.id === capabilityId) ?? tree.toMove.find((c) => c.id === capabilityId) ?? null,
      );
    } catch {
      this.capability.set(null);
    }
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
