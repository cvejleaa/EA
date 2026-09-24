// Typerne genereres fra API-kontrakten (src/api/openapi.json → src/api/schema.d.ts, `npm run gen:api`).
// Skriv aldrig DTO-typer i hånden her: så driver klient og server fra hinanden.
import type { components } from '../../api/schema';

type Schemas = components['schemas'];

export type SystemDetail = Schemas['SystemDetail'];
export type SystemListItem = Schemas['SystemListItem'];
export type SystemListResponse = Schemas['SystemListResponse'];
export type SystemWriteRequest = Schemas['SystemWriteRequest'];
export type SystemRef = Schemas['SystemRef'];
export type SystemPermissions = Schemas['SystemPermissions'];
export type LifecycleStatus = Schemas['LifecycleStatus'];
// OpenAPI beskriver den nullable enum med null som værdi; selve typen er uden null.
export type SystemType = NonNullable<Schemas['SystemType']>;
export type SystemRole = Schemas['SystemRole'];
export type RoleAssignmentInput = Schemas['RoleAssignmentInput'];
export type PersonDto = Schemas['PersonDto'];
export type TeamDto = Schemas['TeamDto'];
export type MeResponse = Schemas['MeResponse'];
export type DevUserResponse = Schemas['DevUserResponse'];
export type DevTokenResponse = Schemas['DevTokenResponse'];
export type AuthModeResponse = Schemas['AuthModeResponse'];
export type CreatePersonRequest = Schemas['CreatePersonRequest'];
export type SystemLink = Schemas['SystemLink'];
export type IntegrationDto = Schemas['IntegrationDto'];
export type SystemIntegrationItem = Schemas['SystemIntegrationItem'];
export type SystemIntegrationsResponse = Schemas['SystemIntegrationsResponse'];
export type IntegrationSummary = Schemas['IntegrationSummary'];
export type IntegrationCreateRequest = Schemas['IntegrationCreateRequest'];
export type IntegrationUpdateRequest = Schemas['IntegrationUpdateRequest'];
export type IntegrationRelation = Schemas['IntegrationRelation'];
export type IntegrationType = NonNullable<Schemas['IntegrationType']>;
export type DataObjectDto = Schemas['DataObjectDto'];
export type CapabilityNode = Schemas['CapabilityNode'];
export type CapabilityTreeResponse = Schemas['CapabilityTreeResponse'];
export type CapabilityImportResult = Schemas['CapabilityImportResult'];
export type CapabilityImportSummary = Schemas['CapabilityImportSummary'];
export type CapabilityChange = Schemas['CapabilityChange'];
export type CapabilityChangeKind = Schemas['CapabilityChangeKind'];
export type CapabilitySnapshot = Schemas['CapabilitySnapshot'];
export type ImportRowError = Schemas['ImportRowError'];
export type CapabilityRef = Schemas['CapabilityRef'];
export type SystemCapabilityDto = Schemas['SystemCapabilityDto'];
export type CapabilityToMove = Schemas['CapabilityToMove'];
export type CoupledSystem = Schemas['CoupledSystem'];
// Nullable i kontrakten ("ingen grund"); selve typen er uden null.
export type MoveReason = NonNullable<Schemas['MoveReason']>;
export type CouplingImportResult = Schemas['CouplingImportResult'];
export type CouplingImportSummary = Schemas['CouplingImportSummary'];
export type CouplingChange = Schemas['CouplingChange'];
export type CouplingChangeKind = Schemas['CouplingChangeKind'];
export type CapabilityOverlap = Schemas['CapabilityOverlap'];
export type OverlapMember = Schemas['OverlapMember'];
export type CouplingCoverage = Schemas['CouplingCoverage'];
// Nullable i kontrakten ("tæller med"); selve typen er uden null.
export type OverlapExclusion = NonNullable<Schemas['OverlapExclusion']>;
