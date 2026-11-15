import { Component, inject, OnInit } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, Observable } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

type AuthMode = 'login' | 'register' | 'forgot' | 'reset' | 'verify';

@Component({
  selector: 'app-auth-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './auth-page.component.html',
  styleUrl: './auth-page.component.scss'
})
export class AuthPageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly mode = this.route.snapshot.data['mode'] as AuthMode;
  readonly token = this.route.snapshot.queryParamMap.get('token') ?? '';

  busy = false;
  error = '';
  message = '';

  readonly loginForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  readonly registerForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    email: ['', [Validators.required, Validators.email]],
    role: ['Student' as 'Student' | 'Mentor', Validators.required],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required]
  });

  readonly forgotForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]]
  });

  readonly resetForm = this.fb.nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required]
  });

  ngOnInit(): void {
    // A verification link should complete verification when opened;
    // the button remains available to retry a temporary API failure.
    if (this.mode === 'verify' && this.token) {
      this.verify();
    }
  }

  submit(): void {
    if (this.busy) return;
    this.error = '';
    this.message = '';

    if (this.mode === 'login') {
      if (this.loginForm.invalid) return this.invalid(this.loginForm);
      this.perform(this.auth.login(this.loginForm.getRawValue()), () => {
        void this.router.navigateByUrl(this.auth.dashboardUrl());
      });
    }

    if (this.mode === 'register') {
      if (this.registerForm.invalid) return this.invalid(this.registerForm);
      const value = this.registerForm.getRawValue();
      if (value.password !== value.confirmPassword) return this.fail('Passwords do not match.');
      if (!this.validPassword(value.password)) return this.fail('Use 8+ characters with at least one letter and one number.');
      this.perform(this.auth.register(value), () => {
        void this.router.navigateByUrl(this.auth.dashboardUrl());
      });
    }

    if (this.mode === 'forgot') {
      if (this.forgotForm.invalid) return this.invalid(this.forgotForm);
      this.perform(this.auth.forgotPassword(this.forgotForm.getRawValue().email), () => {
        this.message = 'If the account exists, password reset instructions have been sent.';
      });
    }

    if (this.mode === 'reset') {
      if (!this.token) return this.fail('This reset link is missing its token. Request a new one.');
      if (this.resetForm.invalid) return this.invalid(this.resetForm);
      const value = this.resetForm.getRawValue();
      if (value.password !== value.confirmPassword) return this.fail('Passwords do not match.');
      if (!this.validPassword(value.password)) return this.fail('Use 8+ characters with at least one letter and one number.');
      this.perform(this.auth.resetPassword(this.token, value.password, value.confirmPassword), () => {
        this.auth.logout();
        void this.router.navigateByUrl('/login');
      });
    }
  }

  verify(): void {
    if (this.busy) return;
    this.error = '';
    if (!this.token) return this.fail('Verification link is missing its token.');
    this.perform(this.auth.verifyEmail(this.token), () => {
      void this.router.navigateByUrl(this.auth.isAuthenticated() ? this.auth.dashboardUrl() : '/login');
    });
  }

  private perform<T>(operation: Observable<T>, onSuccess: (value: T) => void): void {
    this.busy = true;
    operation.pipe(finalize(() => this.busy = false)).subscribe({
      next: result => onSuccess(result),
      error: (err: HttpErrorResponse) => {
        this.error = typeof err.error?.error === 'string'
          ? err.error.error
          : err.status === 0
            ? 'Backend unreachable. Start the API on localhost:5055.'
            : `Request failed (HTTP ${err.status}). Please try again.`;
      }
    });
  }

  private validPassword(value: string): boolean {
    return value.length >= 8 && /[A-Za-z]/.test(value) && /[0-9]/.test(value);
  }

  private invalid(form: { markAllAsTouched(): void }): void {
    form.markAllAsTouched();
    this.fail('Please complete all fields correctly.');
  }

  private fail(message: string): void { this.error = message; }
}