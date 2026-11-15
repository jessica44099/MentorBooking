import { Routes } from '@angular/router';
import { roleGuard } from './core/auth/role.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Home | MentorBooking',
    loadComponent: () => import('./pages/home/home.component').then(m => m.HomeComponent)
  },
  ...(['login', 'register', 'forgot-password', 'reset-password', 'verify-email'] as const)
    .map(path => ({
      path,
      title: `${path.replaceAll('-', ' ')} | MentorBooking`,
      data: { mode: path === 'forgot-password' ? 'forgot' : path === 'reset-password' ? 'reset' : path === 'verify-email' ? 'verify' : path },
      loadComponent: () => import('./pages/auth/auth-page.component').then(m => m.AuthPageComponent)
    })),
  {
    path: 'student/dashboard',
    title: 'Student Dashboard | MentorBooking',
    canActivate: [roleGuard('Student')],
    loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent)
  },
  {
    path: 'mentor/dashboard',
    title: 'Mentor Dashboard | MentorBooking',
    canActivate: [roleGuard('Mentor')],
    loadComponent: () => import('./pages/mentor-dashboard/mentor-dashboard.component').then(m => m.MentorDashboardComponent)
  },
  {
    path: '**',
    title: 'Page Not Found | MentorBooking',
    loadComponent: () => import('./pages/not-found/not-found.component').then(m => m.NotFoundComponent)
  }
];