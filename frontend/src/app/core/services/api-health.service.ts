import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, Observable, of } from 'rxjs';

export interface ApiHealthResponse {
  status: string;
  application: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ApiHealthService {
  private readonly http = inject(HttpClient);

  check(): Observable<ApiHealthResponse> {
    return this.http.get<ApiHealthResponse>('/api/health').pipe(
      catchError(() => of({
        status: 'Unavailable',
        application: 'MentorBooking.Api',
        message: 'The backend is not reachable. Start it on localhost:5055.'
      }))
    );
  }
}
