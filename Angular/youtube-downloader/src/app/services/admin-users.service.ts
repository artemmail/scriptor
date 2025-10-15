import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdminManualSubscriptionPaymentRequest,
  AdminSubscription,
  AdminUserSubscriptionSummary,
  AdminUsersPage
} from '../models/admin-user.model';

@Injectable({ providedIn: 'root' })
export class AdminUsersService {
  private readonly apiUrl = '/api/admin/users';

  constructor(private readonly http: HttpClient) {}

  getUsers(
    page: number,
    pageSize: number,
    filter?: string,
    sortBy?: string,
    sortOrder?: 'asc' | 'desc'
  ): Observable<AdminUsersPage> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (filter) {
      params = params.set('filter', filter);
    }

    if (sortBy) {
      params = params.set('sortBy', sortBy);
    }

    if (sortOrder) {
      params = params.set('sortOrder', sortOrder);
    }

    return this.http.get<AdminUsersPage>(this.apiUrl, { params });
  }

  getRoles(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/roles`);
  }

  updateUserRoles(userId: string, roles: string[]): Observable<string[]> {
    return this.http.put<string[]>(`${this.apiUrl}/${userId}/roles`, { roles });
  }

  getUserSubscription(userId: string): Observable<AdminUserSubscriptionSummary> {
    return this.http.get<AdminUserSubscriptionSummary>(`${this.apiUrl}/${userId}/subscription`);
  }

  createManualSubscriptionPayment(
    request: AdminManualSubscriptionPaymentRequest
  ): Observable<AdminSubscription> {
    return this.http.post<AdminSubscription>('/api/admin/subscriptions/manual', request);
  }
}
