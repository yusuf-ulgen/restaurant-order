import type {
  CustomerSessionDto,
  ProblemDetailsContract,
} from '@restaurant-order/contracts';

export type SessionErrorType =
  | 'unauthenticated'
  | 'session_expired'
  | 'rate_limited'
  | 'unavailable'
  | 'generic';

export class CustomerSessionApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly errorType: SessionErrorType,
    message: string,
    public readonly retryAfterSeconds?: number
  ) {
    super(message);
    this.name = 'CustomerSessionApiError';
  }
}

// Strictly in-memory session cache. NEVER written to localStorage or sessionStorage.
let inMemorySession: CustomerSessionDto | null = null;

export const getInMemorySession = (): CustomerSessionDto | null => inMemorySession;
export const setInMemorySession = (session: CustomerSessionDto | null): void => {
  inMemorySession = session;
};
export const clearInMemorySession = (): void => {
  inMemorySession = null;
};

export const customerSessionApi = {
  getCurrentSession: async (): Promise<CustomerSessionDto | null> => {
    try {
      const res = await fetch('/api/v1/customer/session', {
        method: 'GET',
        credentials: 'include',
        headers: {
          Accept: 'application/json',
        },
      });

      if (res.status === 401 || res.status === 403) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        clearInMemorySession();
        if (
          problem?.title === 'Session Expired' ||
          problem?.detail?.toLowerCase().includes('closed') ||
          problem?.detail?.toLowerCase().includes('merged')
        ) {
          throw new CustomerSessionApiError(
            401,
            'session_expired',
            problem.detail || 'Oturum süresi doldu veya kapatıldı.'
          );
        }
        return null;
      }

      if (res.status === 429) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract & { retryAfterSeconds?: number };
        throw new CustomerSessionApiError(
          429,
          'rate_limited',
          problem.detail || 'Çok fazla istek yapıldı. Lütfen biraz bekleyin.',
          problem.retryAfterSeconds || 30
        );
      }

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        const isUnavailable = res.status === 503;
        throw new CustomerSessionApiError(
          res.status,
          isUnavailable ? 'unavailable' : 'generic',
          problem.detail || 'Sunucuyla bağlantı kurulamadı.'
        );
      }

      const data: CustomerSessionDto = await res.json();
      setInMemorySession(data);
      return data;
    } catch (err: unknown) {
      if (err instanceof CustomerSessionApiError) {
        throw err;
      }
      throw new CustomerSessionApiError(
        0,
        'unavailable',
        'Sunucuya ulaşılamıyor. Lütfen internet bağlantınızı kontrol edin.'
      );
    }
  },
};
