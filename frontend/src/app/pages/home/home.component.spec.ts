import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('shows connected when the actual health endpoint returns Healthy', async () => {
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/health');
    expect(request.request.method).toBe('GET');
    request.flush({
      status: 'Healthy',
      application: 'MentorBooking.Api',
      message: 'Backend is running successfully'
    });

    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Connected');
    http.verify();
  });
});
