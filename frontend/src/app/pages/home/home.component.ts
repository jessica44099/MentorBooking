import { RouterLink } from '@angular/router';
import { AsyncPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { ApiHealthService } from '../../core/services/api-health.service';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [AsyncPipe, RouterLink],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {
  readonly backendStatus$ = inject(ApiHealthService).check();
}
