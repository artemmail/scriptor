import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { AdminUsersPage } from '../models/admin-user.model';

@Injectable({ providedIn: 'root' })
export class AdminUsersService {
  private readonly apiUrl = '/api/admin/users';

  constructor(private readonly http: HttpClient) {}

  getUsers(page: number, pageSize: number, filter?: string): Observable<AdminUsersPage> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (filter) {
      params = params.set('filter', filter);
    }

    return this.http.get<AdminUsersPage>(this.apiUrl, { params });
  }

  getRoles(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/roles`);
  }

  updateUserRoles(userId: string, roles: string[]): Observable<string[]> {
    return this.http.put<string[]>(`${this.apiUrl}/${userId}/roles`, { roles });
  }
}
