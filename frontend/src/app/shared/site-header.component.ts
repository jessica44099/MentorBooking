import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-site-header',
  standalone: true,
  imports: [RouterLink],
  template: `
    <header class="header">
      <div class="container inner">
        <a class="brand" routerLink="/" aria-label="MentorBooking homepage">
          <span class="mark" aria-hidden="true">M</span>
          MentorBooking
        </a>
        <nav aria-label="Main navigation">
          <a routerLink="/">Home</a>
          <a routerLink="/" fragment="how-it-works">How it works</a>
          <a routerLink="/" fragment="api-status">API status</a>
        </nav>
      </div>
    </header>
  `,
  styles: [`
    .header { background: white; border-bottom: 1px solid var(--border); }
    .inner { min-height: 68px; display: flex; gap: 24px; align-items: center; justify-content: space-between; }
    .brand { display: inline-flex; gap: 10px; align-items: center; color: var(--ink); font-weight: 800; font-size: 19px; text-decoration: none; white-space: nowrap; }
    .mark { display: inline-grid; width: 33px; height: 33px; place-items: center; border-radius: 9px; background: var(--brand); color: white; font-weight: 800; }
    nav { display: flex; gap: 24px; align-items: center; }
    nav a { color: var(--muted); text-decoration: none; font-size: 13px; font-weight: 600; }
    nav a:hover { color: var(--brand); }
    @media (max-width: 570px) { .inner { flex-wrap: wrap; gap: 8px; padding-block: 13px; } nav { width: 100%; justify-content: space-between; gap: 12px; } }
  `]
})
export class SiteHeaderComponent {}
