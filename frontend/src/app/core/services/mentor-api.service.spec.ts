import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MentorApiService } from './mentor-api.service';

describe('MentorApiService', () => {
  let api: MentorApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    api = TestBed.inject(MentorApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the authenticated mentor profile', () => {
    api.profile().subscribe(profile => expect(profile.isComplete).toBe(false));
    const request = http.expectOne('/api/mentors/me/profile');
    expect(request.request.method).toBe('GET');
    request.flush({ mentorId: '1', name: 'Mentor', bio: null, subjects: [], isComplete: false });
  });

  it('sends selected subjects and bio to the real profile endpoint', () => {
    api.updateProfile('I help learners with Quant concepts.', ['Quant']).subscribe();
    const request = http.expectOne('/api/mentors/me/profile');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.subjects).toEqual(['Quant']);
    request.flush({ mentorId: '1', name: 'Mentor', bio: 'I help learners with Quant concepts.', subjects: ['Quant'], isComplete: true });
  });

  it('loads a week using the API UTC query argument', () => {
    const monday = '2026-11-09T18:30:00.000Z';
    api.week(monday).subscribe(slots => expect(slots).toEqual([]));
    const request = http.expectOne(req => req.url === '/api/availability/mine' && req.params.get('weekStartUtc') === monday);
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });
});