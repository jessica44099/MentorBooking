import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AccountRole, AuthService } from './auth.service';

export function roleGuard(requiredRole: AccountRole): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.isAuthenticated()) return router.parseUrl('/login');
    if (auth.role() !== requiredRole) return router.parseUrl(auth.dashboardUrl());
    return true;
  };
}