import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { AuthService, AuthResponse } from './auth.service';

describe('AuthService', () => {
  beforeEach(() => {
    sessionStorage.removeItem('mentorbooking.session');
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    sessionStorage.removeItem('mentorbooking.session');
  });

  it('logs in and keeps the role supplied by the API', () => {
    const auth = TestBed.inject(AuthService);
    const response: AuthResponse = {
      token: 'test-token', expiresAtUtc: new Date(Date.now() + 600000).toISOString(),
      userId: 'test-user', name: 'Student Test', email: 'student@example.com',
      role: 'Student', isEmailVerified: false
    };
    auth.login({ email: 'student@example.com', password: 'Password1' }).subscribe();
    const http = TestBed.inject(HttpTestingController);
    const req = http.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'student@example.com', password: 'Password1' });
    req.flush(response);
    expect(auth.role()).toBe('Student');
    expect(auth.isAuthenticated()).toBeTrue();
    expect(sessionStorage.getItem('mentorbooking.session')).toContain('test-token');
    auth.logout();
    expect(auth.user()).toBeNull();
  });

  it('starts signed out', () => {
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBeFalse();
    expect(auth.token()).toBeNull();
  });
});