import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Observable } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import {
  MentorApiService, MentorProfile, MentorStats, MentorSlot,
  MentorSession, MentorSubject, SaveMentorSlot, SessionChange,
  SessionCompletion, SlotKind
} from '../../core/services/mentor-api.service';

type Tab = 'profile' | 'calendar' | 'sessions';
type SessionAction = 'approve' | 'reject' | 'cancel' | 'complete';

const MINUTE = 60_000;
const DAY = 24 * 60 * MINUTE;
const IST_OFFSET = 330 * MINUTE;

@Component({
  selector: 'app-mentor-dashboard',
  standalone: true,
  imports: [DatePipe, NgTemplateOutlet, FormsModule, RouterLink],
  templateUrl: './mentor-dashboard.component.html',
  styleUrl: './mentor-dashboard.component.scss'
})
export class MentorDashboardComponent implements OnInit {
  private readonly api = inject(MentorApiService);
  readonly auth = inject(AuthService);

  activeTab: Tab = 'profile';
  loadingProfile = true;
  savingProfile = false;
  sendingVerification = false;
  loadingCalendar = false;
  savingSlot = false;
  loadingSessions = false;
  processingSession = false;
  error = '';
  success = '';

  profile: MentorProfile | null = null;
  stats: MentorStats | null = null;
  bio = '';
  subjects: MentorSubject[] = [];
  readonly subjectOptions: MentorSubject[] = ['Quant', 'VARC', 'DILR'];

  slots: MentorSlot[] = [];
  sessions: MentorSession[] = [];
  readonly halfHours = Array.from({ length: 20 }, (_, index) => index);
  readonly weekDays = Array.from({ length: 7 }, (_, index) => index);
  weekStartUtc = this.currentWeekStart();

  editorOpen = false;
  editingSlot: MentorSlot | null = null;
  editorStartUtc = '';
  editorMinutes = 30;
  editorType: SlotKind = 'Available';
  editorNote = '';

  actionId: string | null = null;
  actionKind: SessionAction | null = null;
  actionText = '';
  completionDoubts = 1;
  completionSummary = '';
  completionResolution: SessionCompletion['resolution'] = 'FullyResolved';

  ngOnInit(): void {
    this.loadProfile();
    this.loadStats();
    this.loadSessions();
    this.loadCalendar();
  }

  get requests(): MentorSession[] {
    return this.sessions.filter(s => s.status === 'Pending');
  }

  get upcoming(): MentorSession[] {
    return this.sessions.filter(s =>
      ['Approved', 'InProgress', 'NeedsReschedule'].includes(s.status));
  }

  get history(): MentorSession[] {
    return this.sessions.filter(s =>
      !['Pending', 'Approved', 'InProgress', 'NeedsReschedule'].includes(s.status));
  }

  showTab(tab: Tab): void {
    if (tab !== 'profile' && !this.profile?.isComplete) {
      this.activeTab = 'profile';
      this.error = 'Complete and save your mentor profile first.';
      return;
    }
    this.clearMessages();
    this.activeTab = tab;
    if (tab === 'calendar') this.loadCalendar();
    if (tab === 'sessions') this.loadSessions();
  }

  resendVerification(): void {
    if (this.sendingVerification) return;
    this.sendingVerification = true;
    this.clearMessages();
    this.auth.resendVerification()
      .pipe(finalize(() => this.sendingVerification = false))
      .subscribe({
        next: response => this.success = response.message,
        error: err => this.error = this.errorMessage(
          err, 'Could not send verification email.')
      });
  }

  loadProfile(): void {
    this.loadingProfile = true;
    this.api.profile().pipe(finalize(() => this.loadingProfile = false)).subscribe({
      next: response => {
        this.profile = response;
        this.bio = response.bio ?? '';
        this.subjects = [...response.subjects];
        if (response.isComplete) this.activeTab = 'calendar';
      },
      error: err => this.error = this.errorMessage(err, 'Could not load mentor profile.')
    });
  }

  toggleSubject(subject: MentorSubject): void {
    this.subjects = this.subjects.includes(subject)
      ? this.subjects.filter(x => x !== subject)
      : [...this.subjects, subject];
  }

  saveProfile(): void {
    if (this.savingProfile) return;
    this.clearMessages();
    const bio = this.bio.trim();
    if (bio.length < 20 || bio.length > 2000 || this.subjects.length === 0) {
      this.error = 'Select at least one subject and write a bio of 20â€“2000 characters.';
      return;
    }
    this.savingProfile = true;
    this.api.updateProfile(bio, this.subjects)
      .pipe(finalize(() => this.savingProfile = false))
      .subscribe({
        next: response => {
          this.profile = response;
          this.bio = response.bio ?? '';
          this.subjects = [...response.subjects];
          this.success = 'Your profile was saved successfully.';
        },
        error: err => this.error = this.errorMessage(err, 'Could not save mentor profile.')
      });
  }

  loadStats(): void {
    this.api.stats().subscribe({
      next: response => this.stats = response,
      error: () => this.stats = null
    });
  }

  loadSessions(): void {
    this.loadingSessions = true;
    this.api.sessions().pipe(finalize(() => this.loadingSessions = false)).subscribe({
      next: response => this.sessions = response,
      error: err => this.error = this.errorMessage(err, 'Could not load your sessions.')
    });
  }

  loadCalendar(): void {
    this.loadingCalendar = true;
    this.api.week(this.weekStartUtc.toISOString())
      .pipe(finalize(() => this.loadingCalendar = false)).subscribe({
        next: response => this.slots = response,
        error: err => this.error = this.errorMessage(err, 'Could not load availability.')
      });
  }

  changeWeek(offset: number): void {
    this.weekStartUtc = new Date(this.weekStartUtc.getTime() + offset * 7 * DAY);
    this.editorOpen = false;
    this.clearMessages();
    this.loadCalendar();
    this.loadSessions();
  }

  goToday(): void {
    this.weekStartUtc = this.currentWeekStart();
    this.editorOpen = false;
    this.clearMessages();
    this.loadCalendar();
    this.loadSessions();
  }

  weekDayLabel(day: number): string {
    return new Intl.DateTimeFormat('en-IN', {
      weekday: 'short', day: 'numeric', month: 'short', timeZone: 'Asia/Kolkata'
    }).format(new Date(this.weekStartUtc.getTime() + day * DAY + 12 * 60 * MINUTE));
  }

  weekLabel(): string {
    return `${this.weekDayLabel(0)} â€“ ${this.weekDayLabel(6)}`;
  }

  hourLabel(index: number): string {
    const hours = 10 + Math.floor(index / 2);
    return `${String(hours).padStart(2, '0')}:${index % 2 === 0 ? '00' : '30'}`;
  }

  cellUtc(day: number, halfHour: number): string {
    return new Date(this.weekStartUtc.getTime() + day * DAY +
      (10 * 60 + halfHour * 30) * MINUTE).toISOString();
  }

  private savedSlot(startIso: string): MentorSlot | undefined {
    const start = Date.parse(startIso);
    const end = start + 30 * MINUTE;
    return this.slots.find(slot =>
      Date.parse(slot.startUtc) < end && Date.parse(slot.endUtc) > start);
  }

  private reservedSession(startIso: string): MentorSession | undefined {
    const start = Date.parse(startIso);
    const end = start + 30 * MINUTE;
    return this.sessions.find(s =>
      ['Pending', 'Approved', 'InProgress', 'NeedsReschedule'].includes(s.status) &&
      Date.parse(s.scheduledStartUtc) < end && Date.parse(s.scheduledEndUtc) > start);
  }

  cellStatus(day: number, index: number): string {
    const when = this.cellUtc(day, index);
    if (this.reservedSession(when)) return 'Reserved';
    return this.savedSlot(when)?.type ?? 'Unpublished';
  }

  cellDescription(day: number, index: number): string {
    const when = this.cellUtc(day, index);
    const reserved = this.reservedSession(when);
    if (reserved) return reserved.topic;
    const slot = this.savedSlot(when);
    return slot?.note?.trim() || this.cellStatus(day, index);
  }

  openCell(day: number, index: number): void {
    this.clearMessages();
    const when = this.cellUtc(day, index);
    if (Date.parse(when) <= Date.now()) {
      this.error = 'You can only create or edit future calendar slots.';
      return;
    }
    if (this.reservedSession(when)) {
      this.error = 'This time is reserved by a session. Manage it in My Sessions.';
      return;
    }
    const saved = this.savedSlot(when) ?? null;
    this.editingSlot = saved;
    this.editorStartUtc = saved?.startUtc ?? when;
    this.editorMinutes = saved
      ? Math.round((Date.parse(saved.endUtc) - Date.parse(saved.startUtc)) / MINUTE)
      : 30;
    this.editorType = saved?.type ?? 'Available';
    this.editorNote = saved?.note ?? '';
    this.editorOpen = true;
  }

  closeEditor(): void { this.editorOpen = false; }

  saveSlot(): void {
    if (this.savingSlot || !this.editorOpen) return;
    this.clearMessages();
    const startMs = Date.parse(this.editorStartUtc);
    const duration = Number(this.editorMinutes);
    if (!Number.isFinite(startMs) || startMs <= Date.now() ||
        !Number.isInteger(duration) || duration < 30 || duration > 480 || duration % 15 !== 0) {
      this.error = 'Choose a future slot lasting 30â€“480 minutes in 15-minute increments.';
      return;
    }
    const istStart = new Date(startMs + IST_OFFSET);
    const istEnd = new Date(startMs + duration * MINUTE + IST_OFFSET);
    if (istStart.getUTCDate() !== istEnd.getUTCDate() ||
        istStart.getUTCMonth() !== istEnd.getUTCMonth() ||
        istStart.getUTCFullYear() !== istEnd.getUTCFullYear() ||
        istStart.getUTCHours() < 10 ||
        (istEnd.getUTCHours() * 60 + istEnd.getUTCMinutes()) > 20 * 60) {
      this.error = 'Slots must stay within 10:00 AMâ€“8:00 PM IST on one date.';
      return;
    }
    if (this.editorNote.trim().length > 500) {
      this.error = 'The note cannot exceed 500 characters.';
      return;
    }
    const body: SaveMentorSlot = {
      type: this.editorType,
      startUtc: new Date(startMs).toISOString(),
      endUtc: new Date(startMs + duration * MINUTE).toISOString(),
      note: this.editorNote.trim() || null
    };
    const request = this.editingSlot
      ? this.api.updateSlot(this.editingSlot.id, body)
      : this.api.createSlot(body);
    this.savingSlot = true;
    request.pipe(finalize(() => this.savingSlot = false)).subscribe({
      next: () => {
        this.editorOpen = false;
        this.success = 'Calendar slot saved successfully.';
        this.loadCalendar();
      },
      error: err => this.error = this.errorMessage(err, 'Could not save the calendar slot.')
    });
  }

  deleteSlot(): void {
    if (!this.editingSlot || this.savingSlot) return;
    if (!confirm('Delete this saved calendar slot?')) return;
    this.clearMessages();
    this.savingSlot = true;
    this.api.deleteSlot(this.editingSlot.id)
      .pipe(finalize(() => this.savingSlot = false)).subscribe({
        next: () => {
          this.editorOpen = false;
          this.success = 'Calendar slot deleted.';
          this.loadCalendar();
        },
        error: err => this.error = this.errorMessage(err, 'Could not delete the calendar slot.')
      });
  }

  openAction(sessionId: string, kind: SessionAction): void {
    this.clearMessages();
    this.actionId = sessionId;
    this.actionKind = kind;
    this.actionText = '';
    this.completionDoubts = 1;
    this.completionSummary = '';
    this.completionResolution = 'FullyResolved';
  }

  closeAction(): void {
    this.actionId = null;
    this.actionKind = null;
  }

  submitAction(): void {
    if (!this.actionId || !this.actionKind || this.processingSession) return;
    this.clearMessages();
    const id = this.actionId;
    let request: Observable<SessionChange>;
    switch (this.actionKind) {
      case 'approve':
        if (!this.actionText.trim()) {
          this.error = 'Meeting information is required. Use a Zoom link, phone number or chat instructions according to the session mode.';
          return;
        }
        request = this.api.approve(id, this.actionText.trim());
        break;
      case 'reject':
        request = this.api.reject(id, this.actionText.trim());
        break;
      case 'cancel':
        request = this.api.cancel(id, this.actionText.trim());
        break;
      case 'complete': {
        const summary = this.completionSummary.trim();
        if (summary.length < 1 || summary.length > 500 ||
            !Number.isInteger(Number(this.completionDoubts)) ||
            this.completionDoubts < 0 || this.completionDoubts > 20) {
          this.error = 'Enter 0â€“20 doubts cleared and a summary of 1â€“500 characters.';
          return;
        }
        request = this.api.complete(id, {
          doubtsCleared: Number(this.completionDoubts),
          summary,
          resolution: this.completionResolution,
          privateMentorNote: null
        });
        break;
      }
    }
    this.processingSession = true;
    request.pipe(finalize(() => this.processingSession = false)).subscribe({
      next: result => {
        this.closeAction();
        this.success = result.message;
        this.refreshSessions();
      },
      error: err => this.error = this.errorMessage(err, 'Could not update session.')
    });
  }

  startSession(id: string): void {
    if (this.processingSession) return;
    this.clearMessages();
    this.processingSession = true;
    this.api.start(id).pipe(finalize(() => this.processingSession = false)).subscribe({
      next: result => {
        this.success = result.message;
        this.refreshSessions();
      },
      error: err => this.error = this.errorMessage(err, 'Could not start session.')
    });
  }

  markNoShow(id: string): void {
    if (this.processingSession || !confirm('Mark this session as No Show?')) return;
    this.clearMessages();
    this.processingSession = true;
    this.api.noShow(id).pipe(finalize(() => this.processingSession = false)).subscribe({
      next: result => {
        this.success = result.message;
        this.refreshSessions();
      },
      error: err => this.error = this.errorMessage(err, 'Could not mark No Show.')
    });
  }

  refreshSessions(): void {
    this.loadSessions();
    this.loadStats();
    this.loadCalendar();
  }

  private currentWeekStart(): Date {
    // Convert the current instant to the IST civil date.
    const ist = new Date(Date.now() + IST_OFFSET);
    const weekday = (ist.getUTCDay() + 6) % 7; // Monday = 0
    const mondayUtcMidnight = Date.UTC(
      ist.getUTCFullYear(), ist.getUTCMonth(), ist.getUTCDate() - weekday);
    return new Date(mondayUtcMidnight - IST_OFFSET);
  }

  private clearMessages(): void { this.error = ''; this.success = ''; }

  private errorMessage(err: unknown, fallback: string): string {
    const error = err as HttpErrorResponse;
    const serverMessage = error?.error?.error;
    if (typeof serverMessage === 'string') return serverMessage;
    if (error?.status === 0) return 'Backend unreachable. Run it on localhost:5055.';
    return `${fallback} (HTTP ${error?.status ?? 'unknown'}).`;
  }
}