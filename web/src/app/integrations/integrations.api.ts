import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  DataObjectDto,
  IntegrationCreateRequest,
  IntegrationDto,
  IntegrationUpdateRequest,
  SystemIntegrationsResponse,
} from '../api/types';
import { downloadFile } from '../core/download';

@Injectable({ providedIn: 'root' })
export class IntegrationsApi {
  private readonly http = inject(HttpClient);

  forSystem(systemId: string): Promise<SystemIntegrationsResponse> {
    return firstValueFrom(this.http.get<SystemIntegrationsResponse>(`/api/systems/${systemId}/integrations`));
  }

  get(id: string): Promise<IntegrationDto> {
    return firstValueFrom(this.http.get<IntegrationDto>(`/api/integrations/${id}`));
  }

  create(request: IntegrationCreateRequest): Promise<IntegrationDto> {
    return firstValueFrom(this.http.post<IntegrationDto>('/api/integrations', request));
  }

  update(id: string, request: IntegrationUpdateRequest): Promise<IntegrationDto> {
    return firstValueFrom(this.http.put<IntegrationDto>(`/api/integrations/${id}`, request));
  }

  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/integrations/${id}`));
  }

  dataObjects(): Promise<DataObjectDto[]> {
    return firstValueFrom(this.http.get<DataObjectDto[]>('/api/data-objects'));
  }

  createDataObject(name: string): Promise<DataObjectDto> {
    return firstValueFrom(this.http.post<DataObjectDto>('/api/data-objects', { name }));
  }

  /** CSV i importformatet — for ét system (inkl. moduler) eller alle. */
  downloadCsv(systemId?: string): Promise<void> {
    const params = systemId ? `?${new HttpParams().set('systemId', systemId)}` : '';
    return downloadFile(this.http, `/api/integrations/export.csv${params}`, 'integrationer.csv');
  }

  /** Referencelisten over systemer med fulde navne (til den, der udfylder CSV-skabelonen). */
  downloadSystemList(): Promise<void> {
    return downloadFile(this.http, '/api/systems/export.csv', 'systemer.csv');
  }
}
