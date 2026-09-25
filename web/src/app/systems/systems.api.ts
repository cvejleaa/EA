import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CreatePersonRequest,
  LifecycleStatus,
  PersonDto,
  SystemDetail,
  SystemListResponse,
  SystemRef,
  SystemType,
  SystemWriteRequest,
  TeamDto,
} from '../api/types';

export interface SystemFilter {
  q?: string;
  status?: LifecycleStatus;
  type?: SystemType;
  /** Team-id eller 'none' (uden team). */
  teamId?: string;
  /** Person-id eller 'none' (uden forretningsejer). */
  businessOwnerId?: string;
  /** Kapabilitets-id eller 'none': mangler at blive koblet (hverken systemet eller familien har en), nedlagte undtaget. */
  capabilityId?: string;
  /** 'true': "Mine systemer" — hvor brugeren har en rolle, og deres moduler (ældst bekræftede først). */
  mine?: 'true';
}

@Injectable({ providedIn: 'root' })
export class SystemsApi {
  private readonly http = inject(HttpClient);

  list(filter: SystemFilter): Promise<SystemListResponse> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value) {
        params = params.set(key, value);
      }
    }
    return firstValueFrom(this.http.get<SystemListResponse>('/api/systems', { params }));
  }

  get(id: string): Promise<SystemDetail> {
    return firstValueFrom(this.http.get<SystemDetail>(`/api/systems/${id}`));
  }

  create(request: SystemWriteRequest): Promise<SystemDetail> {
    return firstValueFrom(this.http.post<SystemDetail>('/api/systems', request));
  }

  update(id: string, request: SystemWriteRequest): Promise<SystemDetail> {
    return firstValueFrom(this.http.put<SystemDetail>(`/api/systems/${id}`, request));
  }

  confirm(id: string, version: SystemDetail['version']): Promise<SystemDetail> {
    return firstValueFrom(this.http.post<SystemDetail>(`/api/systems/${id}/confirm`, { version }));
  }

  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/systems/${id}`));
  }

  parentCandidates(forSystemId?: string): Promise<SystemRef[]> {
    const params = forSystemId ? new HttpParams().set('forSystemId', forSystemId) : undefined;
    return firstValueFrom(this.http.get<SystemRef[]>('/api/systems/parent-candidates', { params }));
  }

  teams(): Promise<TeamDto[]> {
    return firstValueFrom(this.http.get<TeamDto[]>('/api/teams'));
  }

  persons(): Promise<PersonDto[]> {
    return firstValueFrom(this.http.get<PersonDto[]>('/api/persons'));
  }

  createPerson(request: CreatePersonRequest): Promise<PersonDto> {
    return firstValueFrom(this.http.post<PersonDto>('/api/persons', request));
  }
}
