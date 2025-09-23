import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
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
}
