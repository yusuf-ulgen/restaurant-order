import type {
  QrResolveResponse,
  QrExchangeResponse,
  ProblemDetailsContract,
} from '@restaurant-order/contracts';

export type QrErrorType =
  | 'invalid'
  | 'revoked'
  | 'session_closed'
  | 'inactive_table'
  | 'rate_limited'
  | 'unavailable'
  | 'generic';

export class CustomerQrApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly errorType: QrErrorType,
    message: string,
    public readonly retryAfterSeconds?: number
  ) {
    super(message);
    this.name = 'CustomerQrApiError';
  }
}

// Strictly in-memory session storage. NEVER written to localStorage or sessionStorage.
let currentSession: QrExchangeResponse | null = null;

export const getCustomerSession = (): QrExchangeResponse | null => currentSession;
export const setCustomerSession = (session: QrExchangeResponse | null): void => {
  currentSession = session;
};
export const clearCustomerSession = (): void => {
  currentSession = null;
};

function parseProblemError(status: number, problem: ProblemDetailsContract & { retryAfterSeconds?: number }): CustomerQrApiError {
  if (status === 429) {
    return new CustomerQrApiError(
      status,
      'rate_limited',
      problem.detail || 'Çok fazla istek yapıldı. Lütfen biraz bekleyin.',
      problem.retryAfterSeconds || 30
    );
  }

  if (status === 503) {
    return new CustomerQrApiError(
      status,
      'unavailable',
      problem.detail || 'Hizmet şu anda kullanılamıyor. Lütfen daha sonra tekrar deneyin.'
    );
  }

  if (status === 410) {
    const isRevoked = (problem.title || '').toLowerCase().includes('revoked') || (problem.detail || '').toLowerCase().includes('revoked');
    return new CustomerQrApiError(
      status,
      isRevoked ? 'revoked' : 'session_closed',
      problem.detail || 'Bu QR kodun veya oturumun süresi dolmuş.'
    );
  }

  if (status === 400) {
    const isInactive = (problem.title || '').toLowerCase().includes('inactive') || (problem.detail || '').toLowerCase().includes('inactive');
    return new CustomerQrApiError(
      status,
      isInactive ? 'inactive_table' : 'invalid',
      problem.detail || 'Geçersiz QR kod.'
    );
  }

  return new CustomerQrApiError(
    status,
    'generic',
    'Sunucuda beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
  );
}

export const customerQrApi = {
  resolveQr: async (token: string): Promise<QrResolveResponse> => {
    const res = await fetch(`/api/v1/qr/resolve?token=${encodeURIComponent(token)}`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });

    if (!res.ok) {
      const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
      throw parseProblemError(res.status, problem);
    }

    return res.json() as Promise<QrResolveResponse>;
  },

  exchangeQr: async (token: string): Promise<QrExchangeResponse> => {
    const res = await fetch('/api/v1/qr/exchange', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      body: JSON.stringify({ token }),
    });

    if (!res.ok) {
      const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract & { retryAfterSeconds?: number };
      throw parseProblemError(res.status, problem);
    }

    const data: QrExchangeResponse = await res.json();
    // Save strictly to in-memory store
    setCustomerSession(data);
    return data;
  },
};
