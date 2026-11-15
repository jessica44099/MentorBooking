import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export type MentorSubject = 'Quant' | 'VARC' | 'DILR';
export type SlotKind = 'Available' | 'Busy';

export interface MentorProfile {
  mentorId: string;
  name: string;
  bio: string | null;
  subjects: MentorSubject[];
  isComplete: boolean;
}

export interface MentorStats {
  mentorId: string;
  completedSessions: number;
  studentsHelped: number;
  doubtsCleared: number;
  hoursMentored: number;
  averageRating: number | null;
  reviewCount: number;
  pendingRequests: number;
  upcomingApprovedSessions: number;
}

export interface MentorSlot {
  id: string;
  mentorId: string;
  type: SlotKind;
  startUtc: string;
  endUtc: string;
  note: string | null;
}

export interface SaveMentorSlot {
  type: SlotKind;
  startUtc: string;
  endUtc: string;
  note: string | null;
}

export interface MentorSession {
  id: string;
  mentorId: string;
  studentId: string;
  slotId: string;
  topic: string;
  description: string;
  mode: string;
  scheduledStartUtc: string;
  scheduledEndUtc: string;
  status: string;
  createdAtUtc: string;
}

export interface SessionChange {
  sessionId: string;
  status: string;
  changedAtUtc: string;
  message: string;
}

export interface SessionCompletion {
  doubtsCleared: number;
  summary: string;
  resolution: 'FullyResolved' | 'PartlyResolved' | 'NeedsFollowUp';
  privateMentorNote: string | null;
}

@Injectable({ providedIn: 'root' })
export class MentorApiService {
  private readonly http = inject(HttpClient);

  profile(): Observable<MentorProfile> {
    return this.http.get<MentorProfile>('/api/mentors/me/profile');
  }

  updateProfile(bio: string, subjects: MentorSubject[]): Observable<MentorProfile> {
    return this.http.put<MentorProfile>('/api/mentors/me/profile', { bio, subjects });
  }

  stats(): Observable<MentorStats> {
    return this.http.get<MentorStats>('/api/mentors/me/stats');
  }

  week(weekStartUtc: string): Observable<MentorSlot[]> {
    const params = new HttpParams().set('weekStartUtc', weekStartUtc);
    return this.http.get<MentorSlot[]>('/api/availability/mine', { params });
  }

  createSlot(payload: SaveMentorSlot): Observable<MentorSlot> {
    return this.http.post<MentorSlot>('/api/availability', payload);
  }

  updateSlot(id: string, payload: SaveMentorSlot): Observable<MentorSlot> {
    return this.http.put<MentorSlot>(`/api/availability/${encodeURIComponent(id)}`, payload);
  }

  deleteSlot(id: string): Observable<void> {
    return this.http.delete<void>(`/api/availability/${encodeURIComponent(id)}`);
  }

  sessions(): Observable<MentorSession[]> {
    return this.http.get<MentorSession[]>('/api/sessions/mine');
  }

  approve(id: string, meetingInfo: string): Observable<SessionChange> {
    return this.http.post<SessionChange>(`/api/sessions/${encodeURIComponent(id)}/approve`, { meetingInfo });
  }

  reject(id: string, reason: string): Observable<SessionChange> {
    return this.http.post<SessionChange>(`/api/sessions/${encodeURIComponent(id)}/reject`, { reason });
  }

  cancel(id: string, reason: string): Observable<SessionChange> {
    return this.http.post<SessionChange>(`/api/sessions/${encodeURIComponent(id)}/cancel`, { reason });
  }

  start(id: string): Observable<SessionChange> {
    return this.http.post<SessionChange>(`/api/sessions/${encodeURIComponent(id)}/start`, {});
  }

  complete(id: string, request: SessionCompletion): Observable<SessionChange> {
    return this.http.post<SessionChange>(`/api/sessions/${encodeURIComponent(id)}/complete`, request);
  }

  noShow(id: string): Observable<SessionChange> {
    return this.http.post<SessionChange>(`/api/sessions/${encodeURIComponent(id)}/no-show`, {});
  }
}