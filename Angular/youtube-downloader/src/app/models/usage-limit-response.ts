export interface UsageLimitResponse {
  message: string;
  paymentUrl?: string | null;
  remainingQuota?: number | null;
}

export function extractUsageLimitResponse(error: unknown): UsageLimitResponse | null {
  const payload = unwrapErrorPayload(error);

  if (!payload) {
    return null;
  }

  if (typeof payload === 'string') {
    return {
      message: payload,
      paymentUrl: '/billing',
      remainingQuota: null,
    };
  }

  if (typeof payload === 'object') {
    const record = payload as Record<string, unknown>;
    const message = readString(record, ['message', 'Message', 'error', 'Error']);

    if (typeof message === 'string' && message.trim().length > 0) {
      const paymentUrl =
        readString(record, ['paymentUrl', 'PaymentUrl', 'payment_url', 'PaymentURL']) ?? '/billing';
      const remaining = readNumber(record, ['remainingQuota', 'RemainingQuota', 'remaining_quota']);

      return {
        message,
        paymentUrl: paymentUrl && paymentUrl.trim().length > 0 ? paymentUrl : '/billing',
        remainingQuota: remaining,
      };
    }
  }

  return null;
}

function readString(record: Record<string, unknown>, keys: string[]): string | null {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string') {
      return value;
    }
  }

  return null;
}

function readNumber(record: Record<string, unknown>, keys: string[]): number | null {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'number') {
      return value;
    }

    if (value == null) {
      return null;
    }

    const parsed = Number(value);
    if (!Number.isNaN(parsed)) {
      return parsed;
    }
  }

  return null;
}

function unwrapErrorPayload(error: unknown): unknown {
  if (!error) {
    return null;
  }

  if (typeof error === 'object' && 'error' in (error as Record<string, unknown>)) {
    const nested = (error as { error?: unknown }).error;
    if (nested != null && nested !== error) {
      return unwrapErrorPayload(nested);
    }
  }

  return error;
}
