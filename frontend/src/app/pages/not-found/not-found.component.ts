
import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="not-found">
      <section class="content">
        <p class="eyebrow">ERROR 404</p>

        <h1>Page not found</h1>

        <p>
          The page you requested doesn't exist
          or may have been moved.
        </p>

        <a routerLink="/" class="home-link">
          Return to homepage
        </a>
      </section>
    </main>
  `,
  styles: [`
    :host {
      display: block;
      min-height: 100vh;
    }

    .not-found {
      min-height: 100vh;
      padding: 24px;
      display: grid;
      place-items: center;
      background: #f8faff;
      font-family: 'Segoe UI', Arial, sans-serif;
    }

    .content {
      max-width: 440px;
      text-align: center;
    }

    .eyebrow {
      color: #4f46e5;
      font-size: 13px;
      font-weight: 800;
      letter-spacing: 2px;
    }

    h1 {
      color: #172033;
      font-size: clamp(30px, 5vw, 44px);
      margin: 12px 0;
    }

    .content > p:not(.eyebrow) {
      color: #64748b;
      line-height: 1.7;
      margin-bottom: 28px;
    }

    .home-link {
      display: inline-block;
      padding: 12px 20px;
      border-radius: 10px;
      color: white;
      background: #4f46e5;
      text-decoration: none;
      font-weight: 600;
    }

    .home-link:hover {
      background: #4338ca;
    }

    .home-link:focus-visible {
      outline: 3px solid #a5b4fc;
      outline-offset: 3px;
    }
  `]
})
export class NotFoundComponent {}
