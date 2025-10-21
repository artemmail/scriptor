export interface AdminYooMoneyOperation {
  operationId?: string | null;
  title?: string | null;
  amount?: number | null;
  dateTime?: string | null;
  status?: string | null;
  additionalData?: Record<string, unknown> | null;
}

export type AdminYooMoneyOperationDetails = AdminYooMoneyOperation;

export type AdminYooMoneyBillDetails = Record<string, unknown>;

export interface AdminPaymentOperationDetails {
  id: string;
  userId?: string | null;
  userEmail?: string | null;
  userDisplayName?: string | null;
  provider?: string | null;
  status?: string | null;
  applied: boolean;
  amount: number;
  currency: string;
  requestedAt?: string | null;
  completedAt?: string | null;
  payload?: string | null;
  externalOperationId?: string | null;
  walletTransactionId?: string | null;
}
