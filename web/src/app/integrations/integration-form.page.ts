import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import type { DataObjectDto, IntegrationDto, IntegrationType, SystemDetail, SystemListItem } from '../api/types';
import { AuthService } from '../core/auth.service';
import { integrationTypeLabels, integrationTypeOptions, systemDisplayName } from '../core/labels';
import { ProblemInfo, STALE_VERSION, toProblem } from '../core/problem';
import { SystemsApi } from '../systems/systems.api';
import { IntegrationsApi } from './integrations.api';

type Direction = 'sender' | 'modtager';

@Component({
  selector: 'ea-integration-form-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
  ],
  templateUrl: './integration-form.page.html',
  styleUrl: './integration-form.page.css',
})
export class IntegrationFormPage implements OnInit {
  private readonly systemsApi = inject(SystemsApi);
  private readonly api = inject(IntegrationsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Route-parameteren :id — systemet, man kom fra. */
  readonly id = input.required<string>();
  /** Route-parameteren :integrationId — tom ved oprettelse. */
  readonly integrationId = input<string>();

  protected readonly typeLabels = integrationTypeLabels;
  protected readonly typeOptions = integrationTypeOptions;
  protected readonly displayName = systemDisplayName;
  protected readonly staleVersion = STALE_VERSION;

  protected readonly form = new FormGroup({
    direction: new FormControl<Direction>('sender', { nonNullable: true }),
    otherSearch: new FormControl('', { nonNullable: true }),
    otherSystemId: new FormControl<string | null>(null, { validators: [Validators.required] }),
    viaPlatformId: new FormControl('', { nonNullable: true }),
    type: new FormControl<IntegrationType | ''>('', { nonNullable: true }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(4000)] }),
    dataObjectIds: new FormControl<string[]>([], { nonNullable: true }),
  });

  protected readonly newDataObject = new FormControl('', { nonNullable: true });

  protected readonly system = signal<SystemDetail | null>(null);
  protected readonly systems = signal<SystemListItem[]>([]);
  protected readonly dataObjects = signal<DataObjectDto[]>([]);
  protected readonly existing = signal<IntegrationDto | null>(null);
  protected readonly search = signal('');
  protected readonly problem = signal<ProblemInfo | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly confirmingDelete = signal(false);
  protected readonly addingDataObject = signal(false);

  protected readonly isEdit = computed(() => !!this.integrationId());
  protected readonly loaded = computed(() => !!this.system() && (!this.isEdit() || !!this.existing()));
  protected readonly canManageDataObjects = computed(() => this.auth.me()?.permissions.canManageDataObjects ?? false);

  /** Platforme (systemtype Platform) — de eneste, en integration kan gå via. */
  protected readonly platforms = computed(() => this.systems().filter((s) => s.type === 'Platform'));

  /** Andre systemer at vælge som den anden ende, filtreret på det indtastede. */
  protected readonly candidates = computed(() => {
    const q = this.search().trim().toLowerCase();
    return this.systems()
      .filter((s) => s.id !== this.id())
      .filter((s) => !q || systemDisplayName(s).toLowerCase().includes(q))
      .slice(0, 50);
  });

  async ngOnInit(): Promise<void> {
    try {
      const integrationId = this.integrationId();
      const [system, systems, dataObjects, existing] = await Promise.all([
        this.systemsApi.get(this.id()),
        this.systemsApi.list({}),
        this.api.dataObjects(),
        integrationId ? this.api.get(integrationId) : Promise.resolve(null),
      ]);
      this.system.set(system);
      this.systems.set(systems.items);
      this.dataObjects.set(dataObjects);
      if (existing) {
        this.fill(existing);
      }
    } catch (e) {
      this.loadError.set(toProblem(e).message);
    }
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    // Et system skal vælges fra listen — fritekst er ikke et system.
    this.form.controls.otherSystemId.setValue(null);
  }

  protected pickOther(system: SystemListItem): void {
    this.form.controls.otherSystemId.setValue(system.id);
    this.form.controls.otherSearch.setValue(systemDisplayName(system));
  }

  protected fieldError(field: string): string | null {
    return this.problem()?.fieldErrors[field]?.[0] ?? null;
  }

  protected async save(): Promise<void> {
    if (!this.isEdit() && this.form.controls.otherSystemId.invalid) {
      this.form.markAllAsTouched();
      this.problem.set({ status: 0, message: 'Vælg det andet system fra listen.', fieldErrors: {} });
      return;
    }
    this.saving.set(true);
    this.problem.set(null);
    const v = this.form.getRawValue();
    const common = {
      viaPlatformId: v.viaPlatformId || null,
      type: v.type || null,
      name: v.name.trim() || null,
      description: v.description.trim() || null,
      dataObjectIds: v.dataObjectIds,
    };
    try {
      const existing = this.existing();
      if (existing) {
        await this.api.update(existing.id, { ...common, version: existing.version });
      } else {
        const sends = v.direction === 'sender';
        await this.api.create({
          ...common,
          fromSystemId: sends ? this.id() : v.otherSystemId,
          toSystemId: sends ? v.otherSystemId : this.id(),
        });
      }
      await this.router.navigate(['/systemer', this.id()]);
    } catch (e) {
      this.problem.set(toProblem(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected async reload(): Promise<void> {
    const existing = this.existing();
    if (existing) {
      this.problem.set(null);
      this.fill(await this.api.get(existing.id));
    }
  }

  protected async delete(): Promise<void> {
    const existing = this.existing();
    if (!existing) {
      return;
    }
    try {
      await this.api.delete(existing.id);
      await this.router.navigate(['/systemer', this.id()]);
    } catch (e) {
      this.problem.set(toProblem(e));
    }
  }

  protected async addDataObject(): Promise<void> {
    const name = this.newDataObject.value.trim();
    if (!name) {
      return;
    }
    try {
      const created = await this.api.createDataObject(name);
      this.dataObjects.update((list) => [...list, created].sort((a, b) => a.name.localeCompare(b.name, 'da')));
      this.form.controls.dataObjectIds.setValue([...this.form.controls.dataObjectIds.value, created.id]);
      this.newDataObject.reset();
      this.addingDataObject.set(false);
    } catch (e) {
      this.problem.set(toProblem(e));
    }
  }

  private fill(i: IntegrationDto): void {
    this.existing.set(i);
    this.form.patchValue({
      viaPlatformId: i.via?.id ?? '',
      type: i.type ?? '',
      name: i.name ?? '',
      description: i.description ?? '',
      dataObjectIds: i.dataObjects.map((d) => d.id),
    });
  }
}
