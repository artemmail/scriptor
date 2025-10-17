// src/app/services/AuthService.service.ts
import { Inject, Injectable, PLATFORM_ID } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { tap, catchError } from 'rxjs/operators';
import { jwtDecode } from 'jwt-decode';
import { isPlatformBrowser } from '@angular/common';

export interface TokenResponse { token: string; }
export interface UserInfo {
  id: string;
  name: string;
  displayName: string;
  email: string;
  roles: string[];
  canHideCaptions: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private apiUrl = '/api/account';
  private userSubject = new BehaviorSubject<UserInfo | null>(null);
  user$ = this.userSubject.asObservable();

  private readonly isBrowser: boolean;

  constructor(
    private http: HttpClient,
    @Inject(PLATFORM_ID) platformId: Object
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
    // 1) При старте читаем accessToken и декодируем
    const token = this.getAccessToken();
    if (token) {
      this.decodeAndSaveUser(token);
    }
    // 2) Если уже был сохранён userInfo (например, после перезагрузки)
    const saved = this.readStorage('userInfo');
    if (saved) {
      const parsed = JSON.parse(saved) as Partial<UserInfo> & { name?: string };
      const roles = Array.isArray(parsed.roles) ? parsed.roles : parsed.roles ? [parsed.roles] : [];
      const displayName = parsed.displayName ?? parsed.name ?? '';
      const restored: UserInfo = {
        id: parsed.id ?? '',
        name: displayName,
        displayName,
        email: parsed.email ?? '',
        roles,
        canHideCaptions: !!parsed.canHideCaptions,
      };
      this.userSubject.next(restored);
    }
  }

  private saveUser(user: UserInfo | null) {
    if (!this.isBrowser) {
      this.userSubject.next(user);
      return;
    }

    if (user) {
      window.localStorage.setItem('userInfo', JSON.stringify(user));
    } else {
      window.localStorage.removeItem('userInfo');
    }
    this.userSubject.next(user);
  }

  getAccessToken(): string | null {
    return this.readStorage('accessToken');
  }

  setAccessToken(token: string): void {
    this.writeStorage('accessToken', token);
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

      const rawDisplayName =
        payload.displayName ||
        payload.DisplayName ||
        payload.name ||
        payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'] ||
        '';

      const rawCanHide =
        payload.subscriptionCanHideCaptions ??
        payload['subscriptionCanHideCaptions'];
      const canHideCaptions = typeof rawCanHide === 'string'
        ? rawCanHide.toLowerCase() === 'true'
        : !!rawCanHide;

      const user: UserInfo = {
        id:    payload.sub ?? '',
        name:  rawDisplayName,
        displayName: rawDisplayName,
        email: payload.email || '',
        roles,
        canHideCaptions,
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
      tap(() => this.clearAuthState())
    );
  }

  updateDisplayName(displayName: string): void {
    const current = this.userSubject.value;
    if (!current) {
      return;
    }

    const updated: UserInfo = {
      ...current,
      name: displayName,
      displayName,
    };

    this.saveUser(updated);
  }

  private readStorage(key: string): string | null {
    if (!this.isBrowser) {
      return null;
    }
    try {
      return window.localStorage.getItem(key);
    } catch {
      return null;
    }
  }

  private writeStorage(key: string, value: string): void {
    if (!this.isBrowser) {
      return;
    }
    try {
      window.localStorage.setItem(key, value);
    } catch {
      // ignore storage errors in non-browser environments
    }
  }

  private removeStorageItem(key: string): void {
    if (!this.isBrowser) {
      return;
    }
    try {
      window.localStorage.removeItem(key);
    } catch {
      // ignore storage errors in non-browser environments
    }
  }

  clearAuthState(): void {
    this.removeStorageItem('accessToken');
    this.saveUser(null);
  }
}
