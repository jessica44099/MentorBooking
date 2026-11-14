
import { Routes } from '@angular/router';

export const routes: Routes = [

  // Homepage
  {
    path: '',
    pathMatch: 'full',
    title: 'Home | MentorBooking',
    loadComponent: () =>
      import('./pages/home/home.component')
        .then(m => m.HomeComponent)
  },

  // Unknown URLs
  {
    path: '**',
    title: 'Page Not Found | MentorBooking',
    loadComponent: () =>
      import('./pages/not-found/not-found.component')
        .then(m => m.NotFoundComponent)
  }

];
