import { Component } from '@angular/core';

@Component({
  selector: 'app-site-footer',
  standalone: true,
  template: `
    <footer class="footer">
      <div class="container inner">
        <span>MentorBooking</span>
        <span>Free mentor-led doubt-clearing sessions</span>
      </div>
    </footer>
  `,
  styles: [`
    .footer { background: white; border-top: 1px solid var(--border); }
    .inner { min-height: 64px; display: flex; gap: 10px; justify-content: space-between; align-items: center; color: var(--muted); font-size: 12px; }
    .inner span:first-child { color: var(--ink); font-weight: 800; }
    @media (max-width: 540px) { .inner { padding-block: 16px; flex-direction: column; align-items: flex-start; } }
  `]
})
export class SiteFooterComponent {}
