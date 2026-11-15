import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';

@Component({
  selector: 'app-site-header',
  standalone: true,
  imports: [RouterLink],
  template: `
    <header class="header">
      <div class="container inner">
        <a class="brand" routerLink="/" aria-label="MentorBooking homepage">
          <span class="mark" aria-hidden="true">M</span> MentorBooking
        </a>
        <nav aria-label="Main navigation">
          <a routerLink="/">Home</a>
          @if (auth.isAuthenticated()) {
            <a [routerLink]="auth.dashboardUrl()">My dashboard</a>
            <button type="button" class="nav-action" (click)="logout()">Log out</button>
          } @else {
            <a routerLink="/login">Log in</a>
            <a routerLink="/register" class="join">Join free</a>
          }
        </nav>
      </div>
    </header>
  `,
  styles: [`
    .header { background: white; border-bottom: 1px solid var(--border); }
    .inner { min-height: 68px; display: flex; gap: 24px; align-items: center; justify-content: space-between; }
    .brand { display: inline-flex; gap: 10px; align-items: center; color: var(--ink); font-weight: 800; font-size: 19px; text-decoration: none; white-space: nowrap; }
    .mark { display: inline-grid; width: 33px; height: 33px; place-items: center; border-radius: 9px; background: var(--brand); color: white; font-weight: 800; }
    nav { display: flex; gap: 20px; align-items: center; flex-wrap: wrap; }
    nav a, .nav-action { background: transparent; border: 0; padding: 0; color: var(--muted); text-decoration: none; font-size: 13px; font-weight: 650; cursor: pointer; }
    nav a:hover, .nav-action:hover { color: var(--brand); }
    nav a.join { background: var(--brand); color: white; padding: 10px 13px; border-radius: 9px; }
    @media (max-width: 570px) { .inner { flex-wrap: wrap; gap: 12px; padding-block: 13px; } nav { width: 100%; justify-content: flex-start; gap: 16px; } }
  `]
})
export class SiteHeaderComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }
}