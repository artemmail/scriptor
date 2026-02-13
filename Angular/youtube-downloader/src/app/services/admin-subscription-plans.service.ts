import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface AdminSubscriptionPlan {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  price: number;
  currency: string;
  includedTranscriptionMinutes: number;
  includedVideos: number;
  isActive: boolean;
  priority: number;
}

export interface SaveAdminSubscriptionPlanRequest {
  code: string;
  name: string;
  description?: string | null;
  price: number;
  currency: string;
  includedTranscriptionMinutes: number;
  includedVideos: number;
  isActive: boolean;
  priority: number;
}

@Injectable({ providedIn: 'root' })
export class AdminSubscriptionPlansService {
  private readonly baseUrl = '/api/admin/subscriptions/plans';

  constructor(private readonly http: HttpClient) {}

  getPlans(): Observable<AdminSubscriptionPlan[]> {
    return this.http.get<AdminSubscriptionPlan[]>(this.baseUrl);
  }

  savePlan(planId: string, payload: SaveAdminSubscriptionPlanRequest): Observable<AdminSubscriptionPlan> {
    return this.http.put<AdminSubscriptionPlan>(`${this.baseUrl}/${planId}`, payload);
  }
}
