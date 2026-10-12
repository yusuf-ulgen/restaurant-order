import { fetchWithCsrf } from '@restaurant-order/contracts';
import type {
  RestaurantTableDto,
  CreateTableRequest,
  UpdateTableRequest,
  UpdateTableLayoutRequest,
  TableLayoutBatchItem,
  BranchFloorStatusDto,
  TableQrCodeDto,
  TableQrMetadataDto,
  SessionDynamicQrDto,
  DiningAreaContract,
  ProblemDetailsContract,
} from '@restaurant-order/contracts';

export class FloorApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly isConflict = status === 409,
    public readonly isPreconditionFailed = status === 412
  ) {
    super(message);
    this.name = 'FloorApiError';
  }
}

function sanitizeErrorMessage(status: number, problem: ProblemDetailsContract): string {
  if (status === 500) {
    return 'Sunucuda beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.';
  }
  if (status === 409) {
    return problem.detail || 'Veri çakışması tespit edildi. Kayıt başka bir işlem tarafından güncellenmiş olabilir.';
  }
  if (status === 412) {
    return problem.detail || 'Veri güncelliğini yitirmiş (ETag uyuşmazlığı). Lütfen sayfayı yenileyip tekrar deneyin.';
  }
  if (status === 403) {
    return problem.detail || 'Bu işlem için yetkiniz bulunmamaktadır.';
  }
  if (status === 404) {
    return problem.detail || 'İstenen kaynak bulunamadı.';
  }
  return problem.detail || problem.title || `İşlem başarısız oldu (${status}).`;
}

async function request<T>(
  path: string,
  method = 'GET',
  body?: unknown,
  etag?: string
): Promise<T> {
  const headers = new Headers();
  if (etag) {
    headers.set('If-Match', `"${etag}"`);
  }

  const response = await fetchWithCsrf(path, {
    method,
    headers,
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as ProblemDetailsContract;
    const message = sanitizeErrorMessage(response.status, problem);
    throw new FloorApiError(response.status, message);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

const floorBase = (branchId: string) => `/api/v1/floor/branches/${branchId}`;

export const floorApi = {
  getDiningAreas: (branchId: string): Promise<DiningAreaContract[]> =>
    request<DiningAreaContract[]>(`/api/v1/restaurant-config/branches/${branchId}/dining-areas`),

  getTables: (
    branchId: string,
    diningAreaId?: string,
    isActive?: boolean
  ): Promise<RestaurantTableDto[]> => {
    const params = new URLSearchParams();
    if (diningAreaId) params.set('diningAreaId', diningAreaId);
    if (isActive !== undefined) params.set('isActive', String(isActive));
    const qs = params.toString();
    return request<RestaurantTableDto[]>(`${floorBase(branchId)}/tables${qs ? `?${qs}` : ''}`);
  },

  getTable: (branchId: string, tableId: string): Promise<RestaurantTableDto> =>
    request<RestaurantTableDto>(`${floorBase(branchId)}/tables/${tableId}`),

  createTable: (branchId: string, data: CreateTableRequest): Promise<RestaurantTableDto> =>
    request<RestaurantTableDto>(`${floorBase(branchId)}/tables`, 'POST', data),

  updateTable: (
    branchId: string,
    tableId: string,
    data: UpdateTableRequest,
    etag?: string
  ): Promise<RestaurantTableDto> =>
    request<RestaurantTableDto>(
      `${floorBase(branchId)}/tables/${tableId}`,
      'PUT',
      { ...data, concurrencyToken: etag },
      etag
    ),

  updateTableLayout: (
    branchId: string,
    tableId: string,
    data: UpdateTableLayoutRequest,
    etag?: string
  ): Promise<RestaurantTableDto> =>
    request<RestaurantTableDto>(
      `${floorBase(branchId)}/tables/${tableId}/layout`,
      'PUT',
      { ...data, concurrencyToken: etag },
      etag
    ),

  batchUpdateLayout: (
    branchId: string,
    items: TableLayoutBatchItem[]
  ): Promise<RestaurantTableDto[]> =>
    request<RestaurantTableDto[]>(`${floorBase(branchId)}/tables/batch-layout`, 'PUT', { items }),

  activateTable: (branchId: string, tableId: string, etag: string): Promise<RestaurantTableDto> =>
    request<RestaurantTableDto>(
      `${floorBase(branchId)}/tables/${tableId}/activate`,
      'POST',
      { concurrencyToken: etag },
      etag
    ),

  deactivateTable: (branchId: string, tableId: string, etag: string): Promise<RestaurantTableDto> =>
    request<RestaurantTableDto>(
      `${floorBase(branchId)}/tables/${tableId}/deactivate`,
      'POST',
      { concurrencyToken: etag },
      etag
    ),

  getFloorStatus: (branchId: string): Promise<BranchFloorStatusDto> =>
    request<BranchFloorStatusDto>(`${floorBase(branchId)}/status`),

  getTableStaticQr: (
    branchId: string,
    tableId: string,
    format?: 'svg' | 'json'
  ): Promise<TableQrCodeDto> => {
    const qs = format ? `?format=${format}` : '';
    return request<TableQrCodeDto>(`${floorBase(branchId)}/tables/${tableId}/qr${qs}`);
  },

  getTableStaticQrSvg: async (branchId: string, tableId: string): Promise<string> => {
    const res = await fetchWithCsrf(`${floorBase(branchId)}/tables/${tableId}/qr?format=svg`);
    if (!res.ok) {
      throw new FloorApiError(res.status, 'QR SVG verisi alınamadı.');
    }
    return res.text();
  },

  getTableQrMetadata: (branchId: string, tableId: string): Promise<TableQrMetadataDto> =>
    request<TableQrMetadataDto>(`${floorBase(branchId)}/tables/${tableId}/qr/metadata`),

  rotateTableQr: (branchId: string, tableId: string, etag: string): Promise<RestaurantTableDto> =>
    request<RestaurantTableDto>(
      `${floorBase(branchId)}/tables/${tableId}/qr/rotate`,
      'POST',
      { concurrencyToken: etag },
      etag
    ),

  getSessionDynamicQr: (
    branchId: string,
    sessionId: string,
    format?: 'svg' | 'json'
  ): Promise<SessionDynamicQrDto> => {
    const qs = format ? `?format=${format}` : '';
    return request<SessionDynamicQrDto>(`${floorBase(branchId)}/sessions/${sessionId}/qr${qs}`, 'POST');
  },
};
