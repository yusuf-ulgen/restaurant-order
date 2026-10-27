import React from 'react';
import type { CustomerOrderResponseDto } from '@restaurant-order/contracts';
import { Card } from '@restaurant-order/ui';

export interface ActiveOrderStatusCardProps {
  orders: CustomerOrderResponseDto[];
}

export const ActiveOrderStatusCard: React.FC<ActiveOrderStatusCardProps> = ({ orders }) => {
  if (!orders || orders.length === 0) return null;

  const latestOrder = orders[0];
  if (!latestOrder) return null;

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'InPreparation':
        return { bg: '#fef3c7', text: '#92400e' };
      case 'Ready':
        return { bg: '#dcfce7', text: '#166534' };
      case 'Served':
        return { bg: '#f3f4f6', text: '#374151' };
      default:
        return { bg: '#dbeafe', text: '#1e40af' };
    }
  };

  const badgeColors = getStatusColor(latestOrder.status);

  return (
    <div data-testid="active-order-status-card" style={{ marginTop: 'var(--ro-space-4)' }}>
      <Card padding="md">
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            marginBottom: 'var(--ro-space-2)',
          }}
        >
          <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 600 }}>Son Sipariş Durumu</h3>
          <span
            data-testid="active-order-badge"
            style={{
              padding: '4px 8px',
              borderRadius: '4px',
              fontSize: '0.85rem',
              fontWeight: 600,
              backgroundColor: badgeColors.bg,
              color: badgeColors.text,
            }}
          >
            {latestOrder.status}
          </span>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-2)' }}>
          {latestOrder.items.map((item) => (
            <div
              key={item.id}
              data-testid={`active-order-item-${item.id}`}
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                fontSize: '0.875rem',
                padding: '6px 0',
                borderBottom: '1px solid var(--ro-color-border)',
              }}
            >
              <span>
                {item.quantity}x {item.itemName}
              </span>
              <span
                data-testid={`active-item-status-${item.id}`}
                style={{
                  fontSize: '0.8rem',
                  fontWeight: 500,
                  padding: '2px 6px',
                  borderRadius: '4px',
                  backgroundColor: 'var(--ro-color-surface)',
                  border: '1px solid var(--ro-color-border)',
                }}
              >
                {item.status}
              </span>
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
};
