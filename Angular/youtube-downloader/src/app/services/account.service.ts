import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
}

export interface GoogleCalendarStatus {
  isGoogleAccount: boolean;
  isConnected: boolean;
  hasRefreshToken: boolean;
  consentGranted: boolean;
  consentGrantedAt?: string | null;
  consentDeclinedAt?: string | null;
  tokensRevokedAt?: string | null;
  accessTokenUpdatedAt?: string | null;
  accessTokenExpiresAt?: string | null;
  refreshTokenExpiresAt?: string | null;
  canManageFromBot: boolean;
}

export interface GoogleCalendarOperationResponse {
  success: boolean;
  updated: boolean;
  message?: string;
  error?: string;
}

@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly apiUrl = '/api/account';

  constructor(private readonly http: HttpClient) {}

  getProfile(): Observable<UserProfile> {
    return this.http.get<UserProfile>(`${this.apiUrl}/profile`);
  }

  updateProfile(payload: { displayName: string }): Observable<UserProfile> {
    return this.http.put<UserProfile>(`${this.apiUrl}/profile`, payload);
  }

  getGoogleCalendarStatus(): Observable<GoogleCalendarStatus> {
    return this.http.get<GoogleCalendarStatus>(`${this.apiUrl}/google-calendar/status`);
  }

  disconnectGoogleCalendar(): Observable<GoogleCalendarOperationResponse> {
    return this.http.post<GoogleCalendarOperationResponse>(
      `${this.apiUrl}/google-calendar/disconnect`,
      {}
    );
  }
}
