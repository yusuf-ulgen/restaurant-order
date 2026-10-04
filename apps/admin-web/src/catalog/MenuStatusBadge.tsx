import React from 'react';
import { Badge } from '@restaurant-order/ui';

const variants: Record<string, 'primary' | 'success' | 'warning' | 'danger' | 'neutral'> = {
  Active: 'success',
  Draft: 'neutral',
  Archived: 'warning',
};

export const MenuStatusBadge: React.FC<{ status: string }> = ({ status }) => (
  <Badge variant={variants[status] ?? 'neutral'}>{status === 'Active' ? 'Yayında' : status === 'Draft' ? 'Taslak' : status === 'Archived' ? 'Arşivlendi' : status}</Badge>
);
