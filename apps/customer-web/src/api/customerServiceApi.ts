import {
  fetchWithCsrf,
  type ServiceRequestDto,
  type ServiceRequestType,
  type ProblemDetailsContract,
  type DiningSessionDto,
} from '@restaurant-order/contracts';

export class ServiceApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: 'rate_limited' | 'conflict' | 'unauthorized' | 'not_found' | 'bad_request' | 'network_error',
    message: string,
    public readonly retryAfterSeconds?: number
  ) {
    super(message);
    this.name = 'ServiceApiError';
  }
}

export const customerServiceApi = {
  createServiceRequest: async (
    requestType: ServiceRequestType,
    note?: string,
    idempotencyKey?: string
  ): Promise<ServiceRequestDto> => {
    const key = idempotencyKey || (typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : `sr-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`);

    try {
      const res = await fetchWithCsrf('/api/v1/customer/service-requests', {
        method: 'POST',
        credentials: 'include',
        headers: {
          'Idempotency-Key': key,
        },
        body: JSON.stringify({
          requestType,
          note: note?.trim() || null,
        }),
      });

      if (res.status === 429) {
        const retryHeader = res.headers.get('Retry-After');
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract & { retryAfterSeconds?: number };
        const seconds = problem.retryAfterSeconds ?? (retryHeader ? parseInt(retryHeader, 10) : 15);
        throw new ServiceApiError(
          429,
          'rate_limited',
          problem.detail || `Lütfen yeni talep göndermek için ${seconds} saniye bekleyin.`,
          seconds
        );
      }

      if (res.status === 409) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new ServiceApiError(
          409,
          'conflict',
          problem.detail || 'Bu işlem başka bir taleple çakıştı. Lütfen sayfayı yenileyin.'
        );
      }

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new ServiceApiError(
          res.status,
          res.status === 401 || res.status === 403 ? 'unauthorized' : 'bad_request',
          problem.detail || 'Talep iletilemedi.'
        );
      }

      return (await res.json()) as ServiceRequestDto;
    } catch (err: unknown) {
      if (err instanceof ServiceApiError) {
        throw err;
      }
      throw new ServiceApiError(
        0,
        'network_error',
        'Sunucuya ulaşılamıyor. Lütfen internet bağlantınızı kontrol edip tekrar deneyin.'
      );
    }
  },

  getOpenRequests: async (): Promise<ServiceRequestDto[]> => {
    try {
      const res = await fetchWithCsrf('/api/v1/customer/service-requests/open', {
        method: 'GET',
        credentials: 'include',
      });

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new ServiceApiError(
          res.status,
          res.status === 401 || res.status === 403 ? 'unauthorized' : 'bad_request',
          problem.detail || 'Açık talepler yüklenemedi.'
        );
      }

      return (await res.json()) as ServiceRequestDto[];
    } catch (err: unknown) {
      if (err instanceof ServiceApiError) {
        throw err;
      }
      throw new ServiceApiError(0, 'network_error', 'Bağlantı hatası.');
    }
  },

  cancelRequest: async (requestId: string): Promise<ServiceRequestDto> => {
    try {
      const res = await fetchWithCsrf(`/api/v1/customer/service-requests/${requestId}`, {
        method: 'DELETE',
        credentials: 'include',
      });

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new ServiceApiError(
          res.status,
          res.status === 404 ? 'not_found' : 'bad_request',
          problem.detail || 'Talep iptal edilemedi.'
        );
      }

      return (await res.json()) as ServiceRequestDto;
    } catch (err: unknown) {
      if (err instanceof ServiceApiError) {
        throw err;
      }
      throw new ServiceApiError(0, 'network_error', 'Talep iptal edilirken bağlantı hatası oluştu.');
    }
  },

  requestBill: async (
    branchId: string,
    sessionId: string,
    concurrencyToken: string
  ): Promise<DiningSessionDto> => {
    try {
      const res = await fetchWithCsrf(
        `/api/v1/floor/branches/${branchId}/sessions/${sessionId}/request-bill`,
        {
          method: 'POST',
          credentials: 'include',
          headers: {
            'If-Match': `"${concurrencyToken}"`,
          },
          body: JSON.stringify({ concurrencyToken }),
        }
      );

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new ServiceApiError(
          res.status,
          res.status === 409 ? 'conflict' : 'bad_request',
          problem.detail || 'Hesap talebi iletilemedi.'
        );
      }

      return (await res.json()) as DiningSessionDto;
    } catch (err: unknown) {
      if (err instanceof ServiceApiError) {
        throw err;
      }
      throw new ServiceApiError(0, 'network_error', 'Hesap talep edilirken bağlantı hatası oluştu.');
    }
  },
};
