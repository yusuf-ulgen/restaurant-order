/**
/// Floor and table contracts for the restaurant dining area layouts.
*/

export type TableShape = 'Square' | 'Round' | 'Rectangle';

export interface RestaurantTableDto {
  id: string;
  tenantId: string;
  branchId: string;
  diningAreaId: string;
  tableNumber: string;
  name: string;
  capacity: number;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  rotationDegrees: number;
  shape: TableShape;
  isActive: boolean;
  qrVersion: number;
  publicCode: string;
  concurrencyToken: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface CreateTableRequest {
  diningAreaId: string;
  tableNumber: string;
  name: string;
  capacity: number;
  positionX?: number;
  positionY?: number;
  width?: number;
  height?: number;
  rotationDegrees?: number;
  shape?: TableShape;
  branchId?: string;
}

export interface UpdateTableRequest {
  diningAreaId: string;
  tableNumber: string;
  name: string;
  capacity: number;
  concurrencyToken?: string;
}

export interface UpdateTableLayoutRequest {
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  rotationDegrees: number;
  shape: TableShape;
  concurrencyToken?: string;
}

export interface TableLayoutBatchItem {
  tableId: string;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  rotationDegrees: number;
  shape: TableShape;
  concurrencyToken: string;
}

export interface BatchUpdateTableLayoutRequest {
  items: TableLayoutBatchItem[];
}

export type DiningSessionStatus = 'Open' | 'Active' | 'BillRequested' | 'Closed';

export interface DiningSessionDto {
  id: string;
  tenantId: string;
  branchId: string;
  tableId: string;
  status: DiningSessionStatus;
  guestCount: number;
  assignedWaiterId?: string | null;
  openedAtUtc: string;
  activatedAtUtc?: string | null;
  billRequestedAtUtc?: string | null;
  closedAtUtc?: string | null;
  closeReason?: string | null;
  mergedIntoSessionId?: string | null;
  concurrencyToken: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface TableFloorStatusDto {
  table: RestaurantTableDto;
  activeSession?: DiningSessionDto | null;
}

export interface BranchFloorStatusDto {
  branchId: string;
  tables: TableFloorStatusDto[];
}

export interface OpenDiningSessionRequest {
  guestCount: number;
  assignedWaiterId?: string | null;
}

export interface CloseDiningSessionRequest {
  reason?: string | null;
  concurrencyToken?: string;
}

export interface TransitionSessionRequest {
  concurrencyToken?: string;
}

export type QrMode = 'static' | 'dynamic';

export interface TableQrCodeDto {
  tableId: string;
  publicCode: string;
  qrVersion: number;
  mode: 'static';
  token: string;
  svg: string;
}

export interface SessionDynamicQrDto {
  sessionId: string;
  tableId: string;
  publicCode: string;
  mode: 'dynamic';
  token: string;
  svg: string;
  expiresAt: string;
}

export interface TableQrMetadataDto {
  tableId: string;
  tableNumber: string;
  name: string;
  publicCode: string;
  qrVersion: number;
  isActive: boolean;
  keyId: string;
}

export interface RotateQrVersionRequest {
  concurrencyToken?: string;
}

export interface QrResolveResponse {
  tenantId: string;
  branchId: string;
  brandName: string;
  branchName: string;
  tableNumber: string;
  tableName: string;
  mode: QrMode;
  hasActiveSession: boolean;
  activeSessionStatus?: DiningSessionStatus | null;
}

export interface QrExchangeRequest {
  token: string;
}

export interface QrExchangeResponse {
  sessionId: string;
  sessionStatus: DiningSessionStatus;
  accessTokenExpiresAt: string;
  tenantId: string;
  branchId: string;
  tableNumber: string;
  tableName: string;
}
