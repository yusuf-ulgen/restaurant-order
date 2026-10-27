import React from 'react';
import { Badge } from '@restaurant-order/ui';
import { StaffOrderItemDto, StaffOrderResponseDto } from '@restaurant-order/contracts';
import { formatMoneyMinorUnits } from './types';

export interface SessionOrderRoundsListProps {
  orders: StaffOrderResponseDto[];
  onCancelItem?: (order: StaffOrderResponseDto, item: StaffOrderItemDto) => void;
}

export const SessionOrderRoundsList: React.FC<SessionOrderRoundsListProps> = ({ orders, onCancelItem }) => {
  const totalSubtotal = orders.reduce((sum, ord) => sum + (ord.subtotalMinorUnits ?? 0), 0);
  const totalTax = orders.reduce((sum, ord) => sum + (ord.taxMinorUnits ?? 0), 0);
  const totalServiceCharge = orders.reduce((sum, ord) => sum + (ord.serviceChargeMinorUnits ?? 0), 0);
  const grandTotal = orders.reduce((sum, ord) => sum + (ord.totalMinorUnits ?? 0), 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      {/* Sipariş Turları & Kalemleri */}
      <div>
        <h3 style={{ fontSize: '1rem', fontWeight: 600, margin: '0 0 8px' }}>
          Siparişler ({orders.length} Tur)
        </h3>
        {orders.length === 0 ? (
          <div
            style={{
              padding: '12px',
              border: '1px dashed #d1d5db',
              borderRadius: '6px',
              textAlign: 'center',
              color: '#6b7280',
            }}
          >
            Henüz verilmiş bir sipariş bulunmamaktadır.
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', maxHeight: '280px', overflowY: 'auto' }}>
            {orders.map((ord) => (
              <div
                key={ord.id}
                style={{
                  border: '1px solid #e5e7eb',
                  borderRadius: '6px',
                  padding: '10px',
                  backgroundColor: '#ffffff',
                }}
              >
                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    marginBottom: '6px',
                    fontSize: '0.85rem',
                    fontWeight: 600,
                  }}
                >
                  <span>Tur #{ord.roundNumber}</span>
                  <Badge variant={ord.status === 'Submitted' ? 'primary' : 'neutral'}>{ord.status}</Badge>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                  {ord.items.map((it) => {
                    const isCancelled = it.status === 'Cancelled';
                    return (
                      <div
                        key={it.id}
                        style={{
                          display: 'flex',
                          justifyContent: 'space-between',
                          alignItems: 'center',
                          fontSize: '0.85rem',
                          textDecoration: isCancelled ? 'line-through' : 'none',
                          opacity: isCancelled ? 0.6 : 1,
                        }}
                      >
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                          <span>
                            {it.quantity}x {it.itemName} {it.variantName ? `(${it.variantName})` : ''}
                          </span>
                          <span style={{ fontSize: '0.75rem', color: '#6b7280' }}>
                            Durum: {it.status}
                          </span>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <span style={{ fontWeight: 600 }}>
                            {formatMoneyMinorUnits(it.lineTotalMinorUnits, ord.currency)}
                          </span>
                          {!isCancelled && onCancelItem && (
                            <button
                              type="button"
                              onClick={() => onCancelItem(ord, it)}
                              aria-label={`${it.itemName} İptal Et`}
                              style={{
                                background: 'transparent',
                                border: '1px solid #f87171',
                                color: '#dc2626',
                                borderRadius: '4px',
                                fontSize: '0.75rem',
                                padding: '2px 6px',
                                cursor: 'pointer',
                              }}
                            >
                              İptal
                            </button>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Gerçek Sipariş Özeti (WAIT-06 Sınırı: Yalnızca Sipariş Toplamı Önizlemesi) */}
      {orders.length > 0 && (
        <div
          style={{
            border: '1px solid #e5e7eb',
            borderRadius: '8px',
            padding: '12px',
            backgroundColor: '#f9fafb',
          }}
        >
          <h4 style={{ margin: '0 0 6px', fontSize: '0.9rem', color: '#374151' }}>Sipariş Toplamı</h4>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', color: '#6b7280' }}>
            <span>Ara Toplam:</span>
            <span>{formatMoneyMinorUnits(totalSubtotal)}</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', color: '#6b7280' }}>
            <span>KDV / Vergi:</span>
            <span>{formatMoneyMinorUnits(totalTax)}</span>
          </div>
          {totalServiceCharge > 0 && (
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', color: '#6b7280' }}>
              <span>Servis Ücreti:</span>
              <span>{formatMoneyMinorUnits(totalServiceCharge)}</span>
            </div>
          )}
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              fontSize: '1.05rem',
              fontWeight: 700,
              marginTop: '6px',
              paddingTop: '6px',
              borderTop: '1px solid #e5e7eb',
            }}
          >
            <span>Genel Toplam:</span>
            <span>{formatMoneyMinorUnits(grandTotal)}</span>
          </div>
        </div>
      )}
    </div>
  );
};
