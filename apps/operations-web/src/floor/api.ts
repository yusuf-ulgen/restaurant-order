import {
  fetchWithCsrf,
  BranchFloorStatusDto,
  DiningSessionDto,
  StaffOrderResponseDto,
  StaffServiceRequestDto,
  DiningAreaContract,
  TransferSessionRequest,
  MergeSessionRequest,
  RuntimeMenuReadModel,
  CreateStaffOrderRequest,
  CancelOrderItemRequest,
  ReadyOrderItemDto,
} from '@restaurant-order/contracts';

export class ApiError extends Error {
  status: number;
  errorCode?: string;
  detail?: string;

  constructor(message: string, status: number, errorCode?: string, detail?: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.errorCode = errorCode;
    this.detail = detail;
  }
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let errorDetail = res.statusText;
    let errorCode: string | undefined;
    try {
      const data = await res.json();
      errorDetail = data.detail || data.title || data.message || errorDetail;
      errorCode = data.errorCode || data.extensions?.errorCode;
    } catch {
      // JSON olmama durumunda varsayılan metin kullanılır
    }

    if (res.status === 409) {
      throw new ApiError(
        errorDetail || 'İşlem çakışması tespit edildi. Başka bir işlem tarafından güncellenmiş olabilir.',
        409,
        errorCode || 'CONCURRENCY_CONFLICT',
        errorDetail
      );
    }

    if (res.status === 403) {
      throw new ApiError(
        errorDetail || 'Bu işlem için yetkiniz bulunmamaktadır.',
        403,
        errorCode || 'FORBIDDEN',
        errorDetail
      );
    }

    throw new ApiError(errorDetail || 'Bir hata oluştu.', res.status, errorCode, errorDetail);
  }

  return res.json() as Promise<T>;
}

export async function fetchFloorStatus(
  branchId: string,
  signal?: AbortSignal
): Promise<BranchFloorStatusDto> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/status`, { signal });
  return handleResponse<BranchFloorStatusDto>(res);
}

export async function fetchDiningAreas(
  branchId: string,
  signal?: AbortSignal
): Promise<DiningAreaContract[]> {
  const res = await fetchWithCsrf(`/api/v1/restaurant-config/branches/${branchId}/dining-areas`, { signal });
  return handleResponse<DiningAreaContract[]>(res);
}

export async function fetchSessionDetail(
  branchId: string,
  sessionId: string,
  signal?: AbortSignal
): Promise<DiningSessionDto> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/sessions/${sessionId}`, { signal });
  return handleResponse<DiningSessionDto>(res);
}

export async function fetchSessionOrders(
  branchId: string,
  sessionId: string,
  signal?: AbortSignal
): Promise<StaffOrderResponseDto[]> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/sessions/${sessionId}/orders`, { signal });
  return handleResponse<StaffOrderResponseDto[]>(res);
}

export async function fetchTableServiceRequests(
  branchId: string,
  tableId?: string,
  sessionId?: string,
  signal?: AbortSignal
): Promise<StaffServiceRequestDto[]> {
  const params = new URLSearchParams();
  if (tableId) params.append('tableId', tableId);
  if (sessionId) params.append('sessionId', sessionId);
  const query = params.toString() ? `?${params.toString()}` : '';

  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/service-requests${query}`, { signal });
  return handleResponse<StaffServiceRequestDto[]>(res);
}

export async function openSession(
  branchId: string,
  tableId: string,
  guestCount: number
): Promise<DiningSessionDto> {
  const idempotencyKey = `open-sess-${tableId}-${Date.now()}`;
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/tables/${tableId}/sessions`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify({ guestCount }),
  });
  return handleResponse<DiningSessionDto>(res);
}

export async function activateSession(
  branchId: string,
  sessionId: string,
  concurrencyToken: string
): Promise<DiningSessionDto> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/sessions/${sessionId}/activate`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'If-Match': `"${concurrencyToken}"`,
    },
    body: JSON.stringify({ concurrencyToken }),
  });
  return handleResponse<DiningSessionDto>(res);
}

export async function requestBill(
  branchId: string,
  sessionId: string,
  concurrencyToken: string
): Promise<DiningSessionDto> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/sessions/${sessionId}/request-bill`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'If-Match': `"${concurrencyToken}"`,
    },
    body: JSON.stringify({ concurrencyToken }),
  });
  return handleResponse<DiningSessionDto>(res);
}

export async function transferSession(
  branchId: string,
  sessionId: string,
  targetTableId: string,
  concurrencyToken: string
): Promise<DiningSessionDto> {
  const idempotencyKey = `trans-${sessionId}-${Date.now()}`;
  const payload: TransferSessionRequest = {
    targetTableId,
    concurrencyToken,
    idempotencyKey,
  };

  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/sessions/${sessionId}/transfer`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'If-Match': `"${concurrencyToken}"`,
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(payload),
  });
  return handleResponse<DiningSessionDto>(res);
}

export async function mergeSession(
  branchId: string,
  sessionId: string,
  targetSessionId: string,
  sourceConcurrencyToken: string,
  targetConcurrencyToken: string
): Promise<DiningSessionDto> {
  const idempotencyKey = `merge-${sessionId}-${Date.now()}`;
  const payload: MergeSessionRequest = {
    targetSessionId,
    sourceConcurrencyToken,
    targetConcurrencyToken,
    idempotencyKey,
  };

  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/sessions/${sessionId}/merge`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'If-Match': `"${sourceConcurrencyToken}"`,
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(payload),
  });
  return handleResponse<DiningSessionDto>(res);
}

export async function acknowledgeServiceRequest(
  branchId: string,
  requestId: string,
  concurrencyToken: string
): Promise<StaffServiceRequestDto> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/service-requests/${requestId}/acknowledge`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'If-Match': `"${concurrencyToken}"`,
    },
    body: JSON.stringify({ concurrencyToken }),
  });
  return handleResponse<StaffServiceRequestDto>(res);
}

export async function completeServiceRequest(
  branchId: string,
  requestId: string,
  concurrencyToken: string
): Promise<StaffServiceRequestDto> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/service-requests/${requestId}/complete`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'If-Match': `"${concurrencyToken}"`,
    },
    body: JSON.stringify({ concurrencyToken }),
  });
  return handleResponse<StaffServiceRequestDto>(res);
}

export async function fetchRuntimeMenu(
  branchId: string,
  signal?: AbortSignal
): Promise<RuntimeMenuReadModel> {
  const res = await fetchWithCsrf(`/api/v1/catalog/branches/${branchId}/runtime-menu`, { signal });
  return handleResponse<RuntimeMenuReadModel>(res);
}

export async function createStaffOrder(
  branchId: string,
  sessionId: string,
  payload: CreateStaffOrderRequest,
  idempotencyKey: string
): Promise<StaffOrderResponseDto> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/sessions/${sessionId}/orders`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(payload),
  });
  return handleResponse<StaffOrderResponseDto>(res);
}

export async function cancelStaffOrderItem(
  branchId: string,
  sessionId: string,
  orderId: string,
  itemId: string,
  payload: CancelOrderItemRequest,
  concurrencyToken?: string
): Promise<StaffOrderResponseDto> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  const token = concurrencyToken || payload.concurrencyToken;
  if (token) {
    headers['If-Match'] = `"${token}"`;
  }

  const res = await fetchWithCsrf(
    `/api/v1/floor/branches/${branchId}/sessions/${sessionId}/orders/${orderId}/items/${itemId}/cancel`,
    {
      method: 'POST',
      headers,
      body: JSON.stringify({
        ...payload,
        concurrencyToken: token,
      }),
    }
  );
  return handleResponse<StaffOrderResponseDto>(res);
}

export async function fetchBranchReadyItems(
  branchId: string,
  signal?: AbortSignal
): Promise<ReadyOrderItemDto[]> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/orders/ready-items`, { signal });
  return handleResponse<ReadyOrderItemDto[]>(res);
}

export async function fetchActiveServiceRequests(
  branchId: string,
  signal?: AbortSignal
): Promise<StaffServiceRequestDto[]> {
  const res = await fetchWithCsrf(`/api/v1/floor/branches/${branchId}/service-requests?limit=100`, { signal });
  return handleResponse<StaffServiceRequestDto[]>(res);
}

