import React from 'react';
import {
  RuntimeMenuReadModel,
  calculateLineItemFinancials,
  calculateOrderTotals,
} from '@restaurant-order/contracts';
import { StaffDraftLine } from './staffOrderDraftStorage';

export interface StaffOrderCartSummaryProps {
  lines: StaffDraftLine[];
  runtimeMenu: RuntimeMenuReadModel;
  onEditLine: (line: StaffDraftLine) => void;
  onRemoveLine: (lineId: string) => void;
  onClearCart: () => void;
  onSubmitOrder: () => void;
  isSubmitting: boolean;
  errorMessage?: string | null;
}

export const StaffOrderCartSummary: React.FC<StaffOrderCartSummaryProps> = ({
  lines,
  runtimeMenu,
  onEditLine,
  onRemoveLine,
  onClearCart,
  onSubmitOrder,
  isSubmitting,
  errorMessage,
}) => {
  // Pure authoritative financial calculation
  const totals = React.useMemo(() => {
    let netSubtotal = 0;
    let taxTotal = 0;

    for (const line of lines) {
      const modifierSum = line.selectedModifiers.reduce((acc, m) => acc + m.priceDeltaMinorUnits, 0);
      const baseUnitPrice = line.unitPriceMinorUnits - modifierSum;

      const fin = calculateLineItemFinancials({
        unitPriceMinorUnits: Math.max(0, baseUnitPrice),
        modifierTotalMinorUnits: modifierSum,
        quantity: line.quantity,
        taxRateBps: runtimeMenu.defaultTaxRateBps || 0,
        pricesIncludeTax: runtimeMenu.pricesIncludeTax,
      });

      netSubtotal += fin.netSubtotalMinorUnits;
      taxTotal += fin.taxMinorUnits;
    }

    const orderTotals = calculateOrderTotals({
      netSubtotalMinorUnits: netSubtotal,
      taxTotalMinorUnits: taxTotal,
      isServiceChargeEnabled: runtimeMenu.isServiceChargeEnabled,
      serviceChargeRateBps: runtimeMenu.serviceChargeRateBps || 0,
    });

    return {
      netSubtotalMinorUnits: netSubtotal,
      taxMinorUnits: taxTotal,
      serviceChargeMinorUnits: orderTotals.serviceChargeMinorUnits,
      grandTotalMinorUnits: orderTotals.grandTotalMinorUnits,
    };
  }, [lines, runtimeMenu]);

  if (lines.length === 0) {
    return (
      <div
        style={{
          padding: '24px 16px',
          textAlign: 'center',
          color: 'var(--ro-color-text-muted, #6b7280)',
          borderTop: '1px solid var(--ro-color-border, #e5e7eb)',
          fontSize: '14px',
        }}
      >
        Henüz bir ürün seçilmedi. Soldan/yukarıdan ürün seçip ekleyin.
      </div>
    );
  }

  return (
    <div
      style={{
        borderTop: '2px solid var(--ro-color-border, #e5e7eb)',
        backgroundColor: 'var(--ro-color-surface, #ffffff)',
        padding: '16px',
        display: 'flex',
        flexDirection: 'column',
        gap: '12px',
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h4 style={{ margin: 0, fontSize: '15px', fontWeight: 600 }}>
          Sipariş Taslağı ({lines.reduce((s, l) => s + l.quantity, 0)} Kalem)
        </h4>
        <button
          type="button"
          disabled={isSubmitting}
          onClick={onClearCart}
          style={{
            background: 'transparent',
            border: 'none',
            color: 'var(--ro-color-error, #dc2626)',
            fontSize: '12px',
            cursor: 'pointer',
            padding: '2px 6px',
          }}
        >
          Taslağı Temizle
        </button>
      </div>

      {/* Lines list */}
      <div style={{ maxHeight: '180px', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '8px' }}>
        {lines.map((line) => (
          <div
            key={line.id}
            style={{
              padding: '8px 10px',
              borderRadius: '6px',
              backgroundColor: 'var(--ro-color-surface-subtle, #f9fafb)',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'flex-start',
              fontSize: '13px',
            }}
          >
            <div style={{ flex: 1, paddingRight: '8px' }}>
              <div style={{ fontWeight: 600 }}>
                {line.quantity}x {line.itemName}
                {line.variantName && ` (${line.variantName})`}
              </div>
              {line.selectedModifiers.length > 0 && (
                <div style={{ fontSize: '11px', color: 'var(--ro-color-text-muted, #6b7280)' }}>
                  {line.selectedModifiers.map((m) => m.name).join(', ')}
                </div>
              )}
              {line.customerNote && (
                <div style={{ fontSize: '11px', fontStyle: 'italic', color: 'var(--ro-color-primary, #2563eb)' }}>
                  Not: {line.customerNote}
                </div>
              )}
            </div>
            <div style={{ textAlign: 'right', display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '4px' }}>
              <span style={{ fontWeight: 600 }}>₺{(line.lineTotalMinorUnits / 100).toFixed(2)}</span>
              <div style={{ display: 'flex', gap: '6px' }}>
                <button
                  type="button"
                  disabled={isSubmitting}
                  onClick={() => onEditLine(line)}
                  style={{
                    background: 'transparent',
                    border: 'none',
                    color: 'var(--ro-color-primary, #2563eb)',
                    fontSize: '11px',
                    cursor: 'pointer',
                    padding: '0 2px',
                  }}
                >
                  Düzenle
                </button>
                <button
                  type="button"
                  disabled={isSubmitting}
                  onClick={() => onRemoveLine(line.id)}
                  style={{
                    background: 'transparent',
                    border: 'none',
                    color: 'var(--ro-color-error, #dc2626)',
                    fontSize: '11px',
                    cursor: 'pointer',
                    padding: '0 2px',
                  }}
                >
                  Sil
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Financial breakdown */}
      <div
        style={{
          borderTop: '1px solid var(--ro-color-border, #e5e7eb)',
          paddingTop: '10px',
          display: 'flex',
          flexDirection: 'column',
          gap: '4px',
          fontSize: '12px',
          color: 'var(--ro-color-text-muted, #6b7280)',
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between' }}>
          <span>Ara Toplam (Net):</span>
          <span>₺{(totals.netSubtotalMinorUnits / 100).toFixed(2)}</span>
        </div>
        <div style={{ display: 'flex', justifyContent: 'space-between' }}>
          <span>KDV Toplamı:</span>
          <span>₺{(totals.taxMinorUnits / 100).toFixed(2)}</span>
        </div>
        {totals.serviceChargeMinorUnits > 0 && (
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <span>Servis Ücreti:</span>
            <span>₺{(totals.serviceChargeMinorUnits / 100).toFixed(2)}</span>
          </div>
        )}
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            fontSize: '15px',
            fontWeight: 700,
            color: 'var(--ro-color-text, #111827)',
            marginTop: '4px',
          }}
        >
          <span>Genel Toplam:</span>
          <span>₺{(totals.grandTotalMinorUnits / 100).toFixed(2)}</span>
        </div>
      </div>

      {errorMessage && (
        <div
          role="alert"
          style={{
            padding: '8px 12px',
            borderRadius: '6px',
            backgroundColor: '#fef2f2',
            color: 'var(--ro-color-error, #dc2626)',
            fontSize: '12px',
            lineHeight: 1.4,
          }}
        >
          {errorMessage}
        </div>
      )}

      {/* Explicit Submit Order Button */}
      <button
        type="button"
        disabled={isSubmitting || lines.length === 0}
        onClick={onSubmitOrder}
        style={{
          width: '100%',
          padding: '12px',
          borderRadius: '8px',
          border: 'none',
          backgroundColor: 'var(--ro-color-primary, #2563eb)',
          color: '#ffffff',
          fontWeight: 700,
          fontSize: '15px',
          cursor: isSubmitting ? 'not-allowed' : 'pointer',
          opacity: isSubmitting ? 0.7 : 1,
          boxShadow: '0 2px 4px rgba(37,99,235,0.2)',
        }}
      >
        {isSubmitting ? 'Sipariş İletiliyor...' : `Siparişi Onayla ve Gönder (₺${(totals.grandTotalMinorUnits / 100).toFixed(2)})`}
      </button>
    </div>
  );
};
