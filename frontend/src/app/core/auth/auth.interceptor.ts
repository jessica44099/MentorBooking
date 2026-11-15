import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { AuthService } from './auth.service';

// Only attach JWTs to our own relative API requests.
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthService).token();
  const publicAuth = /^\/api\/auth\/(login|register|verify-email|forgot-password|reset-password)(?:\?|$)/;
  if (token && request.url.startsWith('/api/') && !publicAuth.test(request.url)) {
    request = request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(request);
};