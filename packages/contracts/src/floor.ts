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
