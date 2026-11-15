import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export type AccountRole = 'Student' | 'Mentor';

export interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  userId: string;
  name: string;
  email: string;
  role: AccountRole;
  isEmailVerified: boolean;
}

export interface RegisterRequest {
  name: string;
  email: string;
  password: string;
  confirmPassword: string;
  role: AccountRole;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface MessageResponse { message: string; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storageKey = 'mentorbooking.session';
  private readonly state = signal<AuthResponse | null>(this.restore());

  readonly user = this.state.asReadonly();
  isAuthenticated(): boolean { return this.token() !== null; }
  readonly role = computed(() => this.user()?.role ?? null);

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', request)
      .pipe(tap(response => this.save(response)));
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', request)
      .pipe(tap(response => this.save(response)));
  }

  resendVerification(): Observable<MessageResponse> {
    return this.http.post<MessageResponse>('/api/auth/resend-verification', {});
  }

  verifyEmail(token: string): Observable<MessageResponse> {
    return this.http.post<MessageResponse>('/api/auth/verify-email', { token })
      .pipe(tap(() => {
        const current = this.user();
        if (current) this.save({ ...current, isEmailVerified: true });
      }));
  }

  forgotPassword(email: string): Observable<MessageResponse> {
    return this.http.post<MessageResponse>('/api/auth/forgot-password', { email });
  }

  resetPassword(token: string, password: string, confirmPassword: string): Observable<MessageResponse> {
    return this.http.post<MessageResponse>('/api/auth/reset-password', {
      token, password, confirmPassword
    });
  }

  token(): string | null {
    const current = this.user();
    if (!current?.token) return null;
    const expires = Date.parse(current.expiresAtUtc);
    return Number.isFinite(expires) && expires > Date.now() ? current.token : null;
  }

  dashboardUrl(): string {
    return this.role() === 'Mentor' ? '/mentor/dashboard' : '/student/dashboard';
  }

  logout(): void {
    this.state.set(null);
    try { sessionStorage.removeItem(this.storageKey); } catch { /* storage unavailable */ }
  }

  private save(value: AuthResponse): void {
    this.state.set(value);
    try { sessionStorage.setItem(this.storageKey, JSON.stringify(value)); }
    catch { /* memory-only session when storage unavailable */ }
  }

  private restore(): AuthResponse | null {
    try {
      const raw = sessionStorage.getItem(this.storageKey);
      if (!raw) return null;
      const value: AuthResponse = JSON.parse(raw);
      if (!value || !['Student', 'Mentor'].includes(value.role) ||
          !value.token || !value.userId || !value.expiresAtUtc ||
          !Number.isFinite(Date.parse(value.expiresAtUtc)) ||
          Date.parse(value.expiresAtUtc) <= Date.now()) {
        sessionStorage.removeItem(this.storageKey);
        return null;
      }
      return value;
    } catch { return null; }
  }
}