import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import type {
  CapabilityNode,
  CapabilityRef,
  LifecycleStatus,
  PersonDto,
  RoleAssignmentInput,
  SystemCapabilityDto,
  SystemDetail,
  SystemRef,
  SystemType,
  SystemWriteRequest,
  TeamDto,
} from '../api/types';
import { AuthService } from '../core/auth.service';
import {
  lifecycleLabels,
  lifecycleOptions,
  moveReasonShortLabels,
  systemTypeLabels,
  systemTypeOptions,
} from '../core/labels';
import { ProblemInfo, STALE_VERSION, toProblem } from '../core/problem';
import { CapabilitiesApi } from '../capabilities/capabilities.api';
import { SystemsApi } from './systems.api';

@Component({
  selector: 'ea-system-form-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatChipsModule,
    MatAutocompleteModule,
  ],
  templateUrl: './system-form.page.html',
  styleUrl: './system-form.page.css',
})
export class SystemFormPage implements OnInit {
  private readonly api = inject(SystemsApi);
  private readonly capabilitiesApi = inject(CapabilitiesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Route-parameteren :id — tom ved oprettelse. */
  readonly id = input<string>();

  protected readonly lifecycleLabels = lifecycleLabels;
  protected readonly lifecycleOptions = lifecycleOptions;
  protected readonly typeLabels = systemTypeLabels;
  protected readonly typeOptions = systemTypeOptions;
  protected readonly staleVersion = STALE_VERSION;
  protected readonly moveReasonShortLabels = moveReasonShortLabels;

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    aliases: new FormControl('', { nonNullable: true }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(4000)] }),
    type: new FormControl<SystemType | ''>('', { nonNullable: true }),
    // Ingen standardværdi: status skal være et aktivt valg.
    lifecycleStatus: new FormControl<LifecycleStatus | null>(null, { validators: [Validators.required] }),
    managingTeamId: new FormControl('', { nonNullable: true }),
    parentSystemId: new FormControl('', { nonNullable: true }),
    businessOwnerId: new FormControl('', { nonNullable: true }),
    systemOwnerId: new FormControl('', { nonNullable: true }),
    stewardIds: new FormControl<string[]>([], { nonNullable: true }),
  });

  protected readonly newPerson = new FormGroup({
    displayName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    department: new FormControl('', { nonNullable: true }),
  });

  protected readonly existing = signal<SystemDetail | null>(null);
  protected readonly teams = signal<TeamDto[]>([]);
  protected readonly persons = signal<PersonDto[]>([]);
  protected readonly parentCandidates = signal<SystemRef[]>([]);
  protected readonly problem = signal<ProblemInfo | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly addingPerson = signal(false);

  /** Kortet (til vælgeren). Kun knuder med `selectable` tilbydes — samme regel som serverens. */
  protected readonly capabilityOptions = signal<CapabilityNode[]>([]);
  /** Systemets EGNE koblinger — det eneste, formularen sender (hele listen, altid). */
  protected readonly ownCapabilities = signal<CapabilityRef[]>([]);
  /** Koblinger via forælder eller moduler — vises, men redigeres på det andet system. */
  protected readonly familyCapabilities = signal<SystemCapabilityDto[]>([]);
  /** Søgeteksten. Et almindeligt felt (ikke en FormControl): feltet tømmes selv efter et valg — se addCapability. */
  protected readonly capabilityQuery = signal('');

  /** Søgning på kode, navn og sti (gruppens navn finder bladene under den). Allerede valgte skjules. */
  protected readonly capabilityMatches = computed(() => {
    const query = this.capabilityQuery().trim().toLowerCase();
    const chosen = new Set(this.ownCapabilities().map((c) => c.id));
    return this.capabilityOptions()
      .filter((c) => c.selectable && !chosen.has(c.id))
      .filter((c) => !query || `${c.code} ${c.name} ${c.path}`.toLowerCase().includes(query))
      .slice(0, 50);
  });

  protected readonly isEdit = computed(() => !!this.id());
  protected readonly canManagePersons = computed(() => this.auth.me()?.permissions.canManagePersons ?? false);
  protected readonly parentBlockedReason = computed(() => this.existing()?.permissions.parentBlockedReason ?? null);

  async ngOnInit(): Promise<void> {
    try {
      const id = this.id();
      const [teams, persons, candidates, capabilities, existing] = await Promise.all([
        this.api.teams(),
        this.api.persons(),
        this.api.parentCandidates(id),
        this.capabilitiesApi.tree(),
        id ? this.api.get(id) : Promise.resolve(null),
      ]);
      this.teams.set(teams);
      this.persons.set(persons);
      this.parentCandidates.set(candidates);
      this.capabilityOptions.set(capabilities.items);
      if (existing) {
        this.fill(existing);
      }
    } catch (e) {
      this.loadError.set(toProblem(e).message);
    }
  }

  protected fieldError(field: string): string | null {
    return this.problem()?.fieldErrors[field]?.[0] ?? null;
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.problem.set({ status: 0, message: 'Udfyld de markerede felter.', fieldErrors: {} });
      return;
    }
    this.saving.set(true);
    this.problem.set(null);
    try {
      const request = this.toRequest();
      const saved = this.existing()
        ? await this.api.update(this.existing()!.id, request)
        : await this.api.create(request);
      await this.router.navigate(['/systemer', saved.id]);
    } catch (e) {
      // Indtastningerne bevares — brugeren skal ikke skrive dem igen.
      this.problem.set(toProblem(e));
    } finally {
      this.saving.set(false);
    }
  }

  /** Efter en konflikt: hent den nyeste version (brugerens ændringer kasseres, det står på knappen). */
  protected async reload(): Promise<void> {
    const id = this.id();
    if (!id) {
      return;
    }
    this.problem.set(null);
    this.fill(await this.api.get(id));
  }

  protected async addPerson(): Promise<void> {
    if (this.newPerson.invalid) {
      this.newPerson.markAllAsTouched();
      return;
    }
    try {
      const person = await this.api.createPerson({
        displayName: this.newPerson.controls.displayName.value,
        department: this.newPerson.controls.department.value || null,
        email: null,
      });
      this.persons.update((list) =>
        [...list, person].sort((a, b) => a.displayName.localeCompare(b.displayName, 'da')),
      );
      this.newPerson.reset();
      this.addingPerson.set(false);
    } catch (e) {
      this.problem.set(toProblem(e));
    }
  }

  protected addCapability(node: CapabilityNode, search: HTMLInputElement): void {
    this.ownCapabilities.update((list) => [
      ...list,
      { id: node.id, code: node.code, name: node.name, path: node.path, moveReason: null },
    ]);
    // Autocomplete har lige skrevet det valgte i feltet; det står allerede som chip.
    search.value = '';
    this.capabilityQuery.set('');
  }

  protected removeCapability(id: string): void {
    this.ownCapabilities.update((list) => list.filter((c) => c.id !== id));
  }

  protected capabilityText(c: { code: string; name: string } | null): string {
    return c ? `${c.code} ${c.name}` : '';
  }

  private fill(s: SystemDetail): void {
    this.existing.set(s);
    this.ownCapabilities.set(s.capabilities.filter((c) => !c.heldBy).map((c) => c.capability));
    this.familyCapabilities.set(s.capabilities.filter((c) => !!c.heldBy));
    const holder = (role: string) => s.roles.filter((r) => r.role === role).map((r) => r.person.id);
    this.form.setValue({
      name: s.name,
      aliases: s.aliases.join(', '),
      description: s.description ?? '',
      type: s.type ?? '',
      lifecycleStatus: s.lifecycleStatus,
      managingTeamId: s.managingTeam?.id ?? '',
      parentSystemId: s.parent?.id ?? '',
      businessOwnerId: holder('Forretningsejer')[0] ?? '',
      systemOwnerId: holder('Systemejer')[0] ?? '',
      stewardIds: holder('Systemforvalter'),
    });
    if (s.permissions.parentBlockedReason) {
      this.form.controls.parentSystemId.disable();
    } else {
      this.form.controls.parentSystemId.enable();
    }
  }

  private toRequest(): SystemWriteRequest {
    const v = this.form.getRawValue();
    const roles: RoleAssignmentInput[] = [
      ...(v.businessOwnerId ? [{ role: 'Forretningsejer' as const, personId: v.businessOwnerId }] : []),
      ...(v.systemOwnerId ? [{ role: 'Systemejer' as const, personId: v.systemOwnerId }] : []),
      ...v.stewardIds.map((personId) => ({ role: 'Systemforvalter' as const, personId })),
    ];
    return {
      name: v.name,
      aliases: v.aliases
        .split(',')
        .map((a) => a.trim())
        .filter((a) => a.length > 0),
      description: v.description.trim() || null,
      type: v.type || null,
      lifecycleStatus: v.lifecycleStatus,
      managingTeamId: v.managingTeamId || null,
      parentSystemId: v.parentSystemId || null,
      roles,
      version: this.existing()?.version ?? null,
      // ALTID en liste (også tom): null betyder "uændret" på serveren.
      capabilityIds: this.ownCapabilities().map((c) => c.id),
    };
  }
}
