import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => http.verify());

  it('sætter Bearer-token på API-kald', () => {
    sessionStorage.setItem('ea.accessToken', 'abc');
    void firstValueFrom(client.get('/api/systems'));
    expect(http.expectOne('/api/systems').request.headers.get('Authorization')).toBe('Bearer abc');
  });

  it('sender ikke token til andre adresser end API\'et', () => {
    sessionStorage.setItem('ea.accessToken', 'abc');
    void firstValueFrom(client.get('https://eksempel.invalid/x'));
    expect(http.expectOne('https://eksempel.invalid/x').request.headers.has('Authorization')).toBe(false);
  });

  it('logger ud og sender til login ved 401', async () => {
    sessionStorage.setItem('ea.accessToken', 'udloebet');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const result = firstValueFrom(client.get('/api/systems')).catch((e: unknown) => e);

    http.expectOne('/api/systems').flush(null, { status: 401, statusText: 'Unauthorized' });
    await result;

    expect(TestBed.inject(AuthService).getToken()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], expect.anything());
  });
});
