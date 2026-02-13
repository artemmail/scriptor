export interface AdminUserListItem {
  id: string;
  email: string;
  displayName: string;
  recognizedVideos: number;
  registeredAt: string;
  roles: string[];
  youtubeCaptionIps: string[];
}

export interface AdminUsersPage {
  items: AdminUserListItem[];
  totalCount: number;
}

export interface AdminSubscriptionPaymentHistoryItem {
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

export interface AdminUserSubscriptionSummary {
  hasActiveSubscription: boolean;
  hasLifetimeAccess: boolean;
  planCode?: string | null;
  planName?: string | null;
  status?: string | number | null;
  endsAt?: string | null;
  isLifetime: boolean;
  freeRecognitionsPerDay: number;
  freeTranscriptionsPerMonth: number;
  freeTranscriptionMinutes: number;
  freeVideos: number;
  remainingTranscriptionMinutes: number;
  remainingVideos: number;
  totalTranscriptionMinutes: number;
  totalVideos: number;
  billingUrl: string;
  payments: AdminSubscriptionPaymentHistoryItem[];
}

export interface AdminManualSubscriptionPaymentRequest {
  userId: string;
  planCode: string;
  amount?: number | null;
  currency?: string | null;
  endDate?: string | null;
  paidAt?: string | null;
  reference?: string | null;
  comment?: string | null;
}

export interface AdminSubscription {
  id: string;
  userId: string;
  userEmail?: string | null;
  planCode: string;
  planName: string;
  status: string | number;
  startDate: string;
  endDate?: string | null;
  isLifetime: boolean;
  autoRenew: boolean;
  externalPaymentId?: string | null;
}
