import type {
  CapabilityImportResult,
  CapabilityImportSummary,
  CapabilityNode,
  CapabilityTreeResponse,
  IntegrationDto,
  MeResponse,
  SystemDetail,
  SystemIntegrationItem,
  SystemIntegrationsResponse,
  SystemLink,
  SystemListItem,
} from '../api/types';

// FIKTIVE testdata.

export function systemDetail(overrides: Partial<SystemDetail> = {}): SystemDetail {
  return {
    id: 'sys-1',
    name: 'Kompas',
    aliases: [],
    description: null,
    type: 'Egenudviklet',
    lifecycleStatus: 'IDrift',
    managingTeam: { id: 'team-1', name: 'Specialiserede Løsninger' },
    parent: null,
    modules: [],
    roles: [],
    createdAt: '2026-01-01T10:00:00Z',
    updatedAt: '2026-08-01T10:00:00Z',
    lastConfirmedAt: '2026-08-01T10:00:00Z',
    lastConfirmedByName: 'Eva Arkitekt',
    version: 7,
    permissions: { canEdit: true, canDelete: true, deleteBlockedReason: null, parentBlockedReason: null },
    ...overrides,
  };
}

export function listItem(overrides: Partial<SystemListItem> = {}): SystemListItem {
  return {
    id: 'sys-1',
    name: 'Kompas',
    parent: null,
    matchedAlias: null,
    type: 'Egenudviklet',
    lifecycleStatus: 'IDrift',
    managingTeam: null,
    businessOwner: null,
    lastConfirmedAt: '2026-08-01T10:00:00Z',
    moduleCount: 0,
    ...overrides,
  };
}

export function me(canEdit: boolean): MeResponse {
  return {
    oid: 'oid-1',
    name: canEdit ? 'Eva Arkitekt' : 'Leo Læser',
    roles: canEdit ? ['EA.Admin'] : [],
    permissions: { canCreateSystems: canEdit, canManagePersons: canEdit, canManageDataObjects: canEdit },
  };
}

/** Tekstindhold uden gentagne mellemrum — så assertions ikke afhænger af skabelonens linjeskift. */
export function text(el: Element | null | undefined): string {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

/** Lad ventende promises (HTTP-svar → signaler) køre færdig, og tegn komponenten igen. */
export async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  for (let i = 0; i < 3; i++) {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
  }
  fixture.detectChanges();
}

export function systemLink(name: string, overrides: Partial<SystemLink> = {}): SystemLink {
  return { id: `id-${name}`, name, parent: null, type: 'Egenudviklet', lifecycleStatus: 'IDrift', ...overrides };
}

export function integration(overrides: Partial<IntegrationDto> = {}): IntegrationDto {
  return {
    id: 'int-1',
    name: null,
    from: systemLink('Kompas'),
    to: systemLink('Laborant'),
    via: null,
    type: 'Api',
    description: null,
    dataObjects: [],
    createdAt: '2026-08-01T10:00:00Z',
    updatedAt: '2026-08-01T10:00:00Z',
    version: 3,
    permissions: { canEdit: true },
    ...overrides,
  };
}

export function integrationItem(overrides: Partial<SystemIntegrationItem> = {}): SystemIntegrationItem {
  return { relation: 'Ud', counterpart: systemLink('Laborant'), localModule: null, integration: integration(), ...overrides };
}

export function integrations(
  items: SystemIntegrationItem[] = [],
  overrides: Partial<SystemIntegrationsResponse> = {},
): SystemIntegrationsResponse {
  return {
    summary: { receivers: 0, suppliers: 0, viaPlatform: 0, localSolutions: 0, directDb: 0 },
    items,
    canAdd: false,
    ...overrides,
  };
}

export function capabilityNode(code: string, depth: number, overrides: Partial<CapabilityNode> = {}): CapabilityNode {
  return { id: `cap-${code}`, code, name: `Navn ${code}`, description: null, parentId: null, depth, ...overrides };
}

export function capabilityTree(items: CapabilityNode[], canImport = true): CapabilityTreeResponse {
  return { items, canImport };
}

export function importSummary(overrides: Partial<CapabilityImportSummary> = {}): CapabilityImportSummary {
  return { new: 0, changed: 0, removed: 0, unchanged: 0, currentTotal: 0, largeRemoval: false, ...overrides };
}

export function importResult(overrides: Partial<CapabilityImportResult> = {}): CapabilityImportResult {
  return { committed: false, errors: [], summary: importSummary(), changes: [], fingerprint: null, ...overrides };
}
