import React, { useMemo } from 'react';
import type { RuntimeMenuReadModel } from '@restaurant-order/contracts';
import { BottomSheet, Button } from '@restaurant-order/ui';
import { useCart } from '../../cart/CartContext';
import { verifyCartAgainstRuntimeMenu } from '../../cart/cartValidator';
import { CartItemRow } from './CartItemRow';
import { formatCurrency } from '../../utils/menuUtils';

export interface CartSheetProps {
  isOpen: boolean;
  onClose: () => void;
  runtimeMenu: RuntimeMenuReadModel | null;
  tableInfo?: { tableNumber: string; tableName: string };
  onNavigateToMenu?: () => void;
}

export const CartSheet: React.FC<CartSheetProps> = ({
  isOpen,
  onClose,
  runtimeMenu,
  tableInfo,
  onNavigateToMenu,
}) => {
  const {
    lines,
    totalItemsCount,
    subtotalMinorUnits,
    currency,
    updateQuantity,
    removeLine,
    clearCart,
  } = useCart();

  // Validate cart lines against live runtime menu
  const verification = useMemo(() => {
    return verifyCartAgainstRuntimeMenu(lines, runtimeMenu);
  }, [lines, runtimeMenu]);

  const isEmpty = lines.length === 0;

  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={onClose}
      title={
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
          <span>Sipariş Sepetim</span>
          <span
            style={{
              fontSize: 'var(--ro-font-size-xs)',
              fontWeight: 'var(--ro-font-weight-regular)',
              color: 'var(--ro-color-text-muted)',
            }}
          >
            ({totalItemsCount} ürün)
          </span>
        </div>
      }
      description={
        tableInfo
          ? `Masa ${tableInfo.tableNumber} (${tableInfo.tableName})`
          : 'Seçtiğiniz lezzetleri gözden geçirin'
      }
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          height: '100%',
          maxHeight: '75vh',
          boxSizing: 'border-box',
        }}
        data-testid="customer-cart-sheet"
      >
        {/* Blocking or Price Changes Warning Alert */}
        {verification.issues.length > 0 && (
          <div
            role="alert"
            style={{
              padding: 'var(--ro-space-3)',
              backgroundColor: verification.hasBlockingIssues
                ? 'var(--ro-color-error-subtle, #fef2f2)'
                : 'var(--ro-color-warning-subtle, #fffbeb)',
              color: verification.hasBlockingIssues
                ? 'var(--ro-color-error-text, #991b1b)'
                : 'var(--ro-color-warning-text, #92400e)',
              borderRadius: 'var(--ro-radius-md)',
              marginBottom: 'var(--ro-space-3)',
              fontSize: 'var(--ro-font-size-xs)',
            }}
            data-testid="cart-staleness-warning"
          >
            <div style={{ fontWeight: 'var(--ro-font-weight-bold)', marginBottom: '2px' }}>
              {verification.hasBlockingIssues
                ? 'Menü Güncellemesi Tespit Edildi'
                : 'Fiyat Bilgilendirmesi'}
            </div>
            <div>
              {verification.hasBlockingIssues
                ? 'Sepetinizdeki bazı ürünler veya seçenekler güncellendi/tükendi. Lütfen sipariş vermeden önce ilgili satırları düzenleyin.'
                : 'Sepetinizdeki bazı ürünlerin fiyatları güncellendi. Yeni fiyatlar geçerlidir.'}
            </div>
          </div>
        )}

        {/* Cart items list or empty state */}
        <div
          style={{
            flex: 1,
            overflowY: 'auto',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-3)',
            paddingRight: 'var(--ro-space-1)',
          }}
          data-testid="cart-items-scroll-container"
        >
          {isEmpty ? (
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                padding: 'var(--ro-space-8) var(--ro-space-4)',
                textAlign: 'center',
                gap: 'var(--ro-space-3)',
              }}
              data-testid="cart-empty-state"
            >
              <span style={{ fontSize: '48px' }} role="img" aria-label="Sepet Boş">
                🛒
              </span>
              <div>
                <h3
                  style={{
                    margin: '0 0 var(--ro-space-1) 0',
                    fontSize: 'var(--ro-font-size-base)',
                    fontWeight: 'var(--ro-font-weight-bold)',
                  }}
                >
                  Sepetiniz Henüz Boş
                </h3>
                <p
                  style={{
                    margin: 0,
                    fontSize: 'var(--ro-font-size-sm)',
                    color: 'var(--ro-color-text-muted)',
                  }}
                >
                  Menüden dilediğiniz lezzetleri seçerek sepetinize ekleyebilirsiniz.
                </p>
              </div>

              <Button
                type="button"
                variant="primary"
                size="md"
                onClick={() => {
                  onClose();
                  onNavigateToMenu?.();
                }}
                data-testid="cart-browse-menu-btn"
                style={{ minHeight: '44px', marginTop: 'var(--ro-space-2)' }}
              >
                Menüyü İncele
              </Button>
            </div>
          ) : (
            lines.map((line) => {
              const lineIssue = verification.issues.find(
                (i) => i.lineSignature === line.configurationSignature
              );

              return (
                <CartItemRow
                  key={line.configurationSignature}
                  line={line}
                  runtimeMenu={runtimeMenu}
                  issue={lineIssue}
                  onUpdateQuantity={updateQuantity}
                  onRemoveLine={removeLine}
                  currency={currency}
                />
              );
            })
          )}
        </div>

        {/* Footer: Subtotal and Phase 8 Gated CTA */}
        {!isEmpty && (
          <div
            style={{
              borderTop: '1px solid var(--ro-color-border)',
              paddingTop: 'var(--ro-space-3)',
              marginTop: 'var(--ro-space-3)',
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--ro-space-2)',
            }}
            data-testid="cart-summary-footer"
          >
            {/* Subtotal row */}
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
              }}
            >
              <span
                style={{
                  fontSize: 'var(--ro-font-size-sm)',
                  color: 'var(--ro-color-text-secondary)',
                }}
              >
                Ara Toplam ({totalItemsCount} ürün):
              </span>
              <span
                style={{
                  fontSize: 'var(--ro-font-size-lg)',
                  fontWeight: 'var(--ro-font-weight-bold)',
                  color: 'var(--ro-color-text-primary)',
                }}
                data-testid="cart-subtotal-display"
              >
                {formatCurrency(subtotalMinorUnits, currency)}
              </span>
            </div>

            {/* Clear Cart Button */}
            <button
              type="button"
              onClick={clearCart}
              data-testid="clear-entire-cart-btn"
              style={{
                background: 'transparent',
                border: 'none',
                color: 'var(--ro-color-text-muted)',
                fontSize: 'var(--ro-font-size-xs)',
                cursor: 'pointer',
                textAlign: 'right',
                padding: '4px 0',
              }}
            >
              Tüm Sepeti Temizle
            </button>

            {/* Phase 8 Border: Order Submission CTA */}
            <Button
              type="button"
              variant="primary"
              size="lg"
              disabled={true}
              aria-disabled="true"
              data-testid="submit-order-cta-btn"
              style={{
                minHeight: '48px',
                width: '100%',
                opacity: 0.7,
                cursor: 'not-allowed',
              }}
            >
              Siparişi Gönder • {formatCurrency(subtotalMinorUnits, currency)}
            </Button>

            {/* Informational Product Copy for Phase 8 Transition */}
            <div
              style={{
                fontSize: '11px',
                color: 'var(--ro-color-text-muted)',
                textAlign: 'center',
                lineHeight: 1.4,
              }}
              data-testid="phase8-order-notice"
            >
              Sepetiniz hazır. Dijital sipariş gönderim akışı Faz 8 kapsamında hizmete sunulacaktır.
            </div>
          </div>
        )}
      </div>
    </BottomSheet>
  );
};
