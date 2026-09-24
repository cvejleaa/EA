import type { MeResponse, SystemDetail, SystemListItem } from '../api/types';

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
