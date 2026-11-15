import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

interface SessionSummary {
  id: string;
  mentorId: string;
  studentId: string;
  topic: string;
  description: string;
  mode: string;
  scheduledStartUtc: string;
  scheduledEndUtc: string;
  status: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly user = this.auth.user;
  readonly role = this.auth.role;
  sessions: SessionSummary[] = [];
  loading = true;
  sending = false;
  error = '';
  message = '';

  ngOnInit(): void { this.loadSessions(); }

  loadSessions(): void {
    this.loading = true;
    this.error = '';
    this.http.get<SessionSummary[]>('/api/sessions/mine')
      .pipe(finalize(() => this.loading = false))
      .subscribe({
        next: items => this.sessions = items,
        error: (err: HttpErrorResponse) => {
          this.error = err.status === 0
            ? 'Cannot reach the backend at localhost:5055.'
            : err.status === 401
              ? 'Your session expired. Please log in again.'
              : typeof err.error?.error === 'string'
                ? err.error.error
                : `Could not load sessions (HTTP ${err.status}).`;
        }
      });
  }

  resendVerification(): void {
    if (this.sending) return;
    this.sending = true;
    this.error = '';
    this.message = '';
    this.auth.resendVerification()
      .pipe(finalize(() => this.sending = false))
      .subscribe({
        next: response => this.message = response.message,
        error: (err: HttpErrorResponse) => {
          this.error = typeof err.error?.error === 'string'
            ? err.error.error : 'Could not request another verification email.';
        }
      });
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}