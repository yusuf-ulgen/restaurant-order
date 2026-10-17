import {
  fetchWithCsrf,
  type CreateCustomerOrderRequest,
  type CustomerOrderResponseDto,
  type CustomerOrderProblemDetails,
  type CustomerOrderErrorCode,
} from '@restaurant-order/contracts';

export class CustomerOrderApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly errorCode: CustomerOrderErrorCode | 'NETWORK_ERROR' | 'UNKNOWN',
    public readonly userMessage: string,
    public readonly problemDetails?: CustomerOrderProblemDetails
  ) {
    super(userMessage);
    this.name = 'CustomerOrderApiError';
  }

  get correlationId(): string | undefined {
    return this.problemDetails?.extensions?.correlationId;
  }

  get affectedMenuItemId(): string | undefined {
    return this.problemDetails?.extensions?.menuItemId;
  }

  get previousPriceMinorUnits(): number | undefined {
    return this.problemDetails?.extensions?.previousPriceMinorUnits;
  }

  get currentPriceMinorUnits(): number | undefined {
    return this.problemDetails?.extensions?.currentPriceMinorUnits;
  }
}

export function mapOrderProblemToUserMessage(
  status: number,
  errorCode?: CustomerOrderErrorCode,
  detailFallback?: string
): string {
  switch (errorCode) {
    case 'ORDER_PRICE_CHANGED':
      return 'Sepetinizdeki bazı ürünlerin fiyatları güncellendi. Lütfen güncel tutarları inceleyip tekrar onaylayınız.';
    case 'ITEM_OUT_OF_STOCK':
      return 'Sepetinizdeki bazı ürünler maalesef tükendi. Lütfen ilgili ürünleri sepetten çıkarınız.';
    case 'INVALID_MODIFIER_SELECTION':
    case 'DUPLICATE_MODIFIER':
      return 'Seçtiğiniz ürün seçeneklerinde bir uyumsuzluk var. Lütfen ürünü tekrar düzenleyiniz.';
    case 'INVALID_VARIANT':
    case 'ITEM_NOT_ACTIVE':
      return 'Seçilen ürün veya porsiyon şu anda aktif değildir.';
    case 'ORDER_TAKING_DISABLED':
      return 'Restoranımız şu anda yeni sipariş kabul etmemektedir.';
    case 'SESSION_NOT_ACTIVE':
      return 'Masa oturumunuz aktif değil veya hesap istendi. Yeni sipariş verilemez.';
    case 'IDEMPOTENCY_CONFLICT':
      return 'Bu sipariş anahtarı ile farklı bir işlem tespit edildi. Lütfen tekrar deneyiniz.';
    case 'DISTRIBUTED_SECURITY_STATE_UNAVAILABLE':
      return 'Güvenlik altyapısı şu anda kullanılamıyor, lütfen kısa bir süre sonra tekrar deneyiniz.';
    case 'CONCURRENCY_CONFLICT':
      return 'Aynı anda başka bir işlem algılandı. Lütfen bir an bekleyip tekrar deneyiniz.';
    default:
      if (status === 401 || status === 403) {
        return 'Oturum süreniz dolmuş veya yetkiniz bulunmamaktadır. Lütfen masadaki QR kodu tekrar okutunuz.';
      }
      if (status === 404) {
        return 'Aradığınız sipariş veya oturum bulunamadı.';
      }
      if (status === 503) {
        return 'Sistem şu anda kullanılamıyor. Sepetiniz korundu, lütfen tekrar deneyiniz.';
      }
      return detailFallback && !detailFallback.includes('exception')
        ? detailFallback
        : 'Siparişiniz iletilemedi. Lütfen internet bağlantınızı kontrol edip tekrar deneyiniz.';
  }
}

export const customerOrderApi = {
  submitOrder: async (
    request: CreateCustomerOrderRequest,
    idempotencyKey: string
  ): Promise<CustomerOrderResponseDto> => {
    try {
      const res = await fetchWithCsrf('/api/v1/customer/orders', {
        method: 'POST',
        credentials: 'include',
        headers: {
          'Idempotency-Key': idempotencyKey,
        },
        body: JSON.stringify(request),
      });

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as CustomerOrderProblemDetails;
        const code = problem.extensions?.errorCode || 'UNKNOWN';
        const userMsg = mapOrderProblemToUserMessage(res.status, code, problem.detail);
        throw new CustomerOrderApiError(res.status, code, userMsg, problem);
      }

      return (await res.json()) as CustomerOrderResponseDto;
    } catch (err: unknown) {
      if (err instanceof CustomerOrderApiError) {
        throw err;
      }
      throw new CustomerOrderApiError(
        0,
        'NETWORK_ERROR',
        'Ağ bağlantısı kurulamadı. Sepetiniz korundu, lütfen internet bağlantınızı kontrol edip tekrar deneyiniz.'
      );
    }
  },

  getOrders: async (): Promise<CustomerOrderResponseDto[]> => {
    try {
      const res = await fetchWithCsrf('/api/v1/customer/orders', {
        method: 'GET',
        credentials: 'include',
      });

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as CustomerOrderProblemDetails;
        const code = problem.extensions?.errorCode || 'UNKNOWN';
        const userMsg = mapOrderProblemToUserMessage(res.status, code, problem.detail);
        throw new CustomerOrderApiError(res.status, code, userMsg, problem);
      }

      return (await res.json()) as CustomerOrderResponseDto[];
    } catch (err: unknown) {
      if (err instanceof CustomerOrderApiError) {
        throw err;
      }
      throw new CustomerOrderApiError(
        0,
        'NETWORK_ERROR',
        'Sipariş geçmişi yüklenemedi. Lütfen internet bağlantınızı kontrol ediniz.'
      );
    }
  },

  getOrderById: async (orderId: string): Promise<CustomerOrderResponseDto> => {
    try {
      const res = await fetchWithCsrf(`/api/v1/customer/orders/${encodeURIComponent(orderId)}`, {
        method: 'GET',
        credentials: 'include',
      });

      if (!res.ok) {
        const problem = (await res.json().catch(() => ({}))) as CustomerOrderProblemDetails;
        const code = problem.extensions?.errorCode || 'UNKNOWN';
        const userMsg = mapOrderProblemToUserMessage(res.status, code, problem.detail);
        throw new CustomerOrderApiError(res.status, code, userMsg, problem);
      }

      return (await res.json()) as CustomerOrderResponseDto;
    } catch (err: unknown) {
      if (err instanceof CustomerOrderApiError) {
        throw err;
      }
      throw new CustomerOrderApiError(
        0,
        'NETWORK_ERROR',
        'Sipariş detayları yüklenemedi. Lütfen internet bağlantınızı kontrol ediniz.'
      );
    }
  },
};
