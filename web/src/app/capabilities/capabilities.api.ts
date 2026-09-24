import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { CapabilityImportResult, CapabilityTreeResponse } from '../api/types';
import { downloadFile } from '../core/download';

@Injectable({ providedIn: 'root' })
export class CapabilitiesApi {
  private readonly http = inject(HttpClient);

  tree(): Promise<CapabilityTreeResponse> {
    return firstValueFrom(this.http.get<CapabilityTreeResponse>('/api/capabilities'));
  }

  downloadModel(): Promise<void> {
    return downloadFile(this.http, '/api/capabilities/export.csv', 'kapabiliteter.csv');
  }

  /**
   * Filen sendes som den er (text/csv). Tør-kørsel gemmer intet; gennemførelse kræver tør-kørslens
   * fingeraftryk, så der gemmes præcis det, brugeren så.
   */
  import(file: Blob, dryRun: boolean, fingerprint?: string): Promise<CapabilityImportResult> {
    let params = new HttpParams().set('dryRun', dryRun);
    if (fingerprint) {
      params = params.set('fingerprint', fingerprint);
    }
    return firstValueFrom(
      this.http.post<CapabilityImportResult>('/api/capabilities/import', file, {
        params,
        headers: { 'Content-Type': 'text/csv' },
      }),
    );
  }
}
