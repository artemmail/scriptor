import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface SubscriptionPlan {
  id: string;
  code: string;
  name: string;
  description?: string;
  billingPeriod: string;
  price: number;
  currency: string;
  maxRecognitionsPerDay?: number | null;
  canHideCaptions: boolean;
  isUnlimitedRecognitions: boolean;
  isLifetime: boolean;
}

export interface PaymentInitResponse {
  operationId: string;
  paymentUrl: string;
}

export interface WalletBalance {
  balance: number;
  currency: string;
}

@Injectable({ providedIn: 'root' })
export class PaymentsService {
  private readonly baseUrl = '/api/payments';

  constructor(private readonly http: HttpClient) {}

  getPlans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(`${this.baseUrl}/plans`);
  }

  getWallet(): Observable<WalletBalance> {
    return this.http.get<WalletBalance>(`${this.baseUrl}/wallet`);
  }

  createSubscription(planCode: string): Observable<PaymentInitResponse> {
    return this.http.post<PaymentInitResponse>(`${this.baseUrl}/subscriptions`, { planCode });
  }

  createWalletDeposit(amount: number, comment?: string): Observable<PaymentInitResponse> {
    return this.http.post<PaymentInitResponse>(`${this.baseUrl}/wallet/deposit`, { amount, comment });
  }
}
