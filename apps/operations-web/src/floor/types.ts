import { TableFloorStatusDto } from '@restaurant-order/contracts';

export type TableOperationalStatus =
  | 'Available'
  | 'Seated'
  | 'OrderPending'
  | 'ReadyToServe'
  | 'BillRequested'
  | 'Inactive';

export interface TableStatusVisual {
  label: string;
  color: string;
  badgeVariant: 'success' | 'primary' | 'warning' | 'danger' | 'neutral';
  icon: string;
  description: string;
}

export const TABLE_STATUS_VISUALS: Record<TableOperationalStatus, TableStatusVisual> = {
  Available: {
    label: 'Boş',
    color: '#10b981',
    badgeVariant: 'success',
    icon: '🟢',
    description: 'Masa müsait ve yeni oturum açılabilir',
  },
  Seated: {
    label: 'Oturuldu',
    color: '#3b82f6',
    badgeVariant: 'primary',
    icon: '👤',
    description: 'Müşteri masada, henüz sipariş verilmedi',
  },
  OrderPending: {
    label: 'Sipariş Hazırlanıyor',
    color: '#f59e0b',
    badgeVariant: 'warning',
    icon: '⏳',
    description: 'Sipariş mutfakta veya barda hazırlanıyor',
  },
  ReadyToServe: {
    label: 'Servise Hazır',
    color: '#8b5cf6',
    badgeVariant: 'primary',
    icon: '🍽️',
    description: 'Hazırlanan ürünler masaya servis edilmeyi bekliyor',
  },
  BillRequested: {
    label: 'Hesap İstendi',
    color: '#ef4444',
    badgeVariant: 'danger',
    icon: '🧾',
    description: 'Müşteri hesap istedi, ödeme veya kapanış bekleniyor',
  },
  Inactive: {
    label: 'Kullanım Dışı',
    color: '#9ca3af',
    badgeVariant: 'neutral',
    icon: '⛔',
    description: 'Masa pasif veya servis dışı',
  },
};

export function resolveTableOperationalStatus(dto: TableFloorStatusDto): TableOperationalStatus {
  if (dto.derivedStatus && dto.derivedStatus in TABLE_STATUS_VISUALS) {
    return dto.derivedStatus as TableOperationalStatus;
  }

  if (!dto.table.isActive) {
    return 'Inactive';
  }

  if (!dto.activeSession) {
    return 'Available';
  }

  if (dto.activeSession.status === 'BillRequested') {
    return 'BillRequested';
  }

  if ((dto.readyItemsCount ?? 0) > 0) {
    return 'ReadyToServe';
  }

  if ((dto.activeOrdersCount ?? 0) > 0) {
    return 'OrderPending';
  }

  return 'Seated';
}

export function formatMoneyMinorUnits(minorUnits?: number | null, currency: string = 'TRY'): string {
  if (minorUnits == null) return '0,00 ₺';
  const major = minorUnits / 100;
  return new Intl.NumberFormat('tr-TR', {
    style: 'currency',
    currency: currency || 'TRY',
  }).format(major);
}

export function calculateSeatedDurationMinutes(createdAtUtc?: string | null): number {
  if (!createdAtUtc) return 0;
  const created = new Date(createdAtUtc).getTime();
  const now = Date.now();
  const diffMinutes = Math.floor((now - created) / 60000);
  return Math.max(0, diffMinutes);
}
