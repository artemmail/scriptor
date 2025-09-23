// src/app/services/AuthService.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { tap, catchError } from 'rxjs/operators';
import { jwtDecode } from 'jwt-decode';

export interface TokenResponse { token: string; }
export interface UserInfo { id: string; name: string; email: string; roles: string[]; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private apiUrl = '/api/account';
  private userSubject = new BehaviorSubject<UserInfo | null>(null);
  user$ = this.userSubject.asObservable();

  constructor(private http: HttpClient) {
    // 1) При старте читаем accessToken и декодируем
    const token = this.getAccessToken();
    if (token) {
      this.decodeAndSaveUser(token);
    }
    // 2) Если уже был сохранён userInfo (например, после перезагрузки)
    const saved = localStorage.getItem('userInfo');
    if (saved) {
      const parsed = JSON.parse(saved);
      if (!Array.isArray(parsed.roles)) {
        parsed.roles = parsed.roles ? [parsed.roles] : [];
      }
      this.userSubject.next(parsed);
    }
  }

  private saveUser(user: UserInfo | null) {
    if (user) {
      localStorage.setItem('userInfo', JSON.stringify(user));
    } else {
      localStorage.removeItem('userInfo');
    }
    this.userSubject.next(user);
  }

  getAccessToken(): string | null {
    return localStorage.getItem('accessToken');
  }

  setAccessToken(token: string): void {
    
    localStorage.setItem('accessToken', token);
    this.decodeAndSaveUser(token);
  }

  private decodeAndSaveUser(token: string) {
    try {
      const payload: any = jwtDecode(token);
      const rawRoles = payload.role
        ?? payload.roles
        ?? payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
      const roles = Array.isArray(rawRoles)
        ? rawRoles
        : rawRoles
          ? [rawRoles]
          : [];

      const user: UserInfo = {
        id:    payload.sub,
        name:  payload.name  || '',
        email: payload.email || '',
        roles
      };
      this.saveUser(user);
    } catch {
      this.saveUser(null);
    }
  }

  /** Обычный логин через форму */
  login(credentials: { email: string; password: string }): Observable<TokenResponse> {
    
    return this.http.post<TokenResponse>(
      `${this.apiUrl}/login`, credentials, { withCredentials: true }
    ).pipe(
      tap(res => this.setAccessToken(res.token))
    );
  }

  /** Обмен access токена по httpOnly refresh-cookie */
  refreshToken(): Observable<TokenResponse> {
    return this.http.post<TokenResponse>(
      `${this.apiUrl}/refresh-token`, {}, { withCredentials: true }
    ).pipe(
      tap(res => this.setAccessToken(res.token))
    );
  }

  /** Полноценный logout: POST /logout, очистка токенов и userInfo */
  logout(): Observable<void> {
    return this.http.post<void>(
      `${this.apiUrl}/logout`, {}, { withCredentials: true }
    ).pipe(
      catchError(() => of(void 0)),
      tap(() => {
        localStorage.removeItem('accessToken');
        this.saveUser(null);
      })
    );
  }
}
