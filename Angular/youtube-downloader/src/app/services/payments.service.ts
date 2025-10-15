import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type SubscriptionBillingPeriod =
  | 'OneTime'
  | 'Monthly'
  | 'Yearly'
  | 'Lifetime'
  | 'ThreeDays'
  | number;

export interface SubscriptionPlan {
  id: string;
  code: string;
  name: string;
  description?: string;
  billingPeriod: SubscriptionBillingPeriod;
  price: number;
  currency: string;
  maxRecognitionsPerDay?: number | null;
  canHideCaptions: boolean;
  isUnlimitedRecognitions: boolean;
  isLifetime: boolean;
}

export interface SubscriptionPaymentHistoryItem {
  invoiceId: string;
  planName?: string | null;
  amount: number;
  currency: string;
  status: string | number;
  issuedAt: string;
  paidAt?: string | null;
  paymentProvider?: string | null;
  externalInvoiceId?: string | null;
  comment?: string | null;
}

export interface SubscriptionSummary {
  hasActiveSubscription: boolean;
  hasLifetimeAccess: boolean;
  planCode?: string | null;
  planName?: string | null;
  status?: string | number | null;
  endsAt?: string | null;
  isLifetime: boolean;
  freeRecognitionsPerDay: number;
  freeTranscriptionsPerMonth: number;
  billingUrl: string;
  payments: SubscriptionPaymentHistoryItem[];
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

  getSubscriptionSummary(): Observable<SubscriptionSummary> {
    return this.http.get<SubscriptionSummary>(`${this.baseUrl}/subscription/summary`);
  }
}
