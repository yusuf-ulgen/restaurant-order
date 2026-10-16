import type {
  RuntimeMenuReadModel,
  ProblemDetailsContract,
} from '@restaurant-order/contracts';

export type RuntimeMenuErrorType =
  | 'unauthenticated'
  | 'session_expired'
  | 'forbidden_scope'
  | 'rate_limited'
  | 'unavailable'
  | 'malformed'
  | 'generic';

export class RuntimeMenuApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly errorType: RuntimeMenuErrorType,
    message: string,
    public readonly retryAfterSeconds?: number
  ) {
    super(message);
    this.name = 'RuntimeMenuApiError';
  }
}

const DEFAULT_TIMEOUT_MS = 10_000;

export const runtimeMenuApi = {
  getRuntimeMenu: async (
    branchId: string,
    signal?: AbortSignal,
    timeoutMs: number = DEFAULT_TIMEOUT_MS
  ): Promise<RuntimeMenuReadModel> => {
    if (!branchId || typeof branchId !== 'string') {
      throw new RuntimeMenuApiError(400, 'malformed', 'Geçersiz şube kimliği.');
    }

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

    const abortListener = () => controller.abort();
    if (signal) {
      signal.addEventListener('abort', abortListener, { once: true });
    }

    try {
      const res = await fetch(`/api/v1/catalog/branches/${encodeURIComponent(branchId)}/runtime-menu`, {
        method: 'GET',
        credentials: 'include',
        signal: controller.signal,
        headers: {
          Accept: 'application/json',
        },
      });

      if (res.status === 401) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new RuntimeMenuApiError(
          401,
          'session_expired',
          problem.detail || 'Oturum süresi doldu veya oturum doğrulanamadı.'
        );
      }

      if (res.status === 403) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new RuntimeMenuApiError(
          403,
          'forbidden_scope',
          problem.detail || 'Bu şubenin menüsüne erişim yetkiniz bulunmamaktadır.'
        );
      }

      if (res.status === 429) {
        const retryHeader = res.headers.get('Retry-After');
        const parsedRetry = retryHeader ? parseInt(retryHeader, 10) : undefined;
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract & { retryAfterSeconds?: number };
        const retryAfter = problem.retryAfterSeconds ?? (Number.isFinite(parsedRetry) ? parsedRetry : 30);
        throw new RuntimeMenuApiError(
          429,
          'rate_limited',
          problem.detail || 'Çok fazla istek yapıldı. Lütfen biraz bekleyin.',
          retryAfter
        );
      }

      if (res.status === 503) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new RuntimeMenuApiError(
          503,
          'unavailable',
          problem.detail || 'Menü servisi şu anda kullanılamıyor. Lütfen az sonra tekrar deneyin.'
        );
      }

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as ProblemDetailsContract;
        throw new RuntimeMenuApiError(
          res.status,
          'generic',
          problem.detail || 'Menü yüklenirken bir hata oluştu.'
        );
      }

      const data = await res.json();

      // Shape validation: ensure required top-level fields are present
      if (!data || typeof data !== 'object' || !Array.isArray(data.menus) || typeof data.currency !== 'string') {
        throw new RuntimeMenuApiError(
          res.status,
          'malformed',
          'Sunucudan gelen menü verisi geçerli bir biçimde değil.'
        );
      }

      return data as RuntimeMenuReadModel;
    } catch (err: unknown) {
      if (err instanceof RuntimeMenuApiError) {
        throw err;
      }

      if (err instanceof DOMException && err.name === 'AbortError') {
        throw new RuntimeMenuApiError(
          408,
          'unavailable',
          'İstek zaman aşımına uğradı veya iptal edildi.'
        );
      }

      throw new RuntimeMenuApiError(
        0,
        'unavailable',
        'Sunucuya ulaşılamıyor. Lütfen internet bağlantınızı kontrol edin.'
      );
    } finally {
      clearTimeout(timeoutId);
      if (signal) {
        signal.removeEventListener('abort', abortListener);
      }
    }
  },
};
