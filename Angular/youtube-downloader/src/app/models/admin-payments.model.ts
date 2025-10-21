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
