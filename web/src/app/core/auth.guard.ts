import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Kræver login. Klientens vagt er kun for brugervenlighed — serveren afviser alt uden gyldigt token. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toLogin = router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

  if (!auth.getToken()) {
    return toLogin;
  }
  if (auth.me()) {
    return true;
  }
  try {
    await auth.loadMe();
    return true;
  } catch {
    auth.logout();
    return toLogin;
  }
};
