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
    const candidate = payload as {
      message?: unknown;
      paymentUrl?: unknown;
      remainingQuota?: unknown;
    };

    if (typeof candidate.message === 'string') {
      return {
        message: candidate.message,
        paymentUrl:
          typeof candidate.paymentUrl === 'string' && candidate.paymentUrl.trim().length > 0
            ? candidate.paymentUrl
            : '/billing',
        remainingQuota:
          typeof candidate.remainingQuota === 'number'
            ? candidate.remainingQuota
            : candidate.remainingQuota == null
              ? null
              : Number(candidate.remainingQuota),
      };
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
