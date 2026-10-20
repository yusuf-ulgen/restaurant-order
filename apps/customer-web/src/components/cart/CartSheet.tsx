import React, { useMemo } from 'react';
import type { RuntimeMenuReadModel } from '@restaurant-order/contracts';
import { calculateCartFinancialSummary } from '@restaurant-order/contracts';
import { BottomSheet, Button } from '@restaurant-order/ui';
import { useCart } from '../../cart/CartContext';
import { verifyCartAgainstRuntimeMenu } from '../../cart/cartValidator';
import { CartItemRow } from './CartItemRow';
import { formatCurrency } from '../../utils/menuUtils';

export interface CartSheetProps {
  isOpen: boolean;
  onClose: () => void;
  runtimeMenu: RuntimeMenuReadModel | null;
  isLoadingMenu?: boolean;
  menuError?: string | null;
  onRetryMenu?: () => void;
  tableInfo?: { tableNumber: string; tableName: string };
  onNavigateToMenu?: () => void;
  onProceedToConfirmation?: () => void;
}

export const CartSheet: React.FC<CartSheetProps> = ({
  isOpen,
  onClose,
  runtimeMenu,
  isLoadingMenu,
  menuError,
  onRetryMenu,
  tableInfo,
  onNavigateToMenu,
  onProceedToConfirmation,
}) => {
  const {
    lines,
    totalItemsCount,
    subtotalMinorUnits,
    currency,
    acceptedPolicyFingerprint,
    requiresReconfirmation,
    updateQuantity,
    removeLine,
    clearCart,
    acceptPriceChanges,
  } = useCart();

  // Validate cart lines against live runtime menu
  const verification = useMemo(() => {
    return verifyCartAgainstRuntimeMenu(lines, runtimeMenu);
  }, [lines, runtimeMenu]);

  const isEmpty = lines.length === 0;

  const currentFinancials = useMemo(() => {
    if (!runtimeMenu || lines.length === 0) return null;
    return calculateCartFinancialSummary(lines, {
      pricesIncludeTax: runtimeMenu.pricesIncludeTax ?? true,
      defaultTaxRateBps: runtimeMenu.defaultTaxRateBps ?? 0,
      isServiceChargeEnabled: runtimeMenu.isServiceChargeEnabled ?? false,
      serviceChargeRateBps: runtimeMenu.serviceChargeRateBps ?? 0,
    });
  }, [lines, runtimeMenu]);

  const isPolicyReconfirmationNeeded =
    !isEmpty &&
    Boolean(runtimeMenu?.pricingPolicyFingerprint) &&
    (acceptedPolicyFingerprint === null ||
      acceptedPolicyFingerprint !== runtimeMenu?.pricingPolicyFingerprint ||
      Boolean(requiresReconfirmation));

  const isMenuMissingOrInvalid = !runtimeMenu || !runtimeMenu.pricingPolicyFingerprint;
  const isOrderBlocked =
    verification.hasBlockingIssues ||
    verification.hasPriceChanges ||
    isPolicyReconfirmationNeeded ||
    Boolean(isLoadingMenu) ||
    Boolean(menuError) ||
    isMenuMissingOrInvalid;

  const canAcceptChanges = !isLoadingMenu && !menuError && Boolean(runtimeMenu?.pricingPolicyFingerprint);

  const handleAcceptPriceChanges = () => {
    if (!runtimeMenu?.pricingPolicyFingerprint || isLoadingMenu || menuError) return;
    const updates = verification.issues
      .filter((i) => i.type === 'price_changed' && i.currentUnitPriceMinorUnits !== undefined)
      .map((i) => ({
        signature: i.lineSignature,
        newUnitPriceMinorUnits: i.currentUnitPriceMinorUnits!,
      }));
    acceptPriceChanges(updates, runtimeMenu.pricingPolicyFingerprint);
  };

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
        {/* Menu Refresh Error Alert */}
        {menuError && (
          <div
            role="alert"
            style={{
              padding: 'var(--ro-space-3)',
              backgroundColor: 'var(--ro-color-error-subtle, #fef2f2)',
              color: 'var(--ro-color-error-text, #991b1b)',
              borderRadius: 'var(--ro-radius-md)',
              marginBottom: 'var(--ro-space-3)',
              fontSize: 'var(--ro-font-size-xs)',
              border: '1px solid var(--ro-color-error-border, #fecaca)',
            }}
            data-testid="cart-menu-error-alert"
          >
            <div style={{ fontWeight: 'var(--ro-font-weight-bold)', marginBottom: '4px' }}>
              Menü Güncelleme Hatası
            </div>
            <div>{menuError}</div>
            {onRetryMenu && (
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={onRetryMenu}
                data-testid="cart-menu-retry-btn"
                style={{ marginTop: 'var(--ro-space-2)', minHeight: '36px' }}
              >
                Tekrar Dene
              </Button>
            )}
          </div>
        )}

        {/* Blocking or Price/Policy Changes Warning Alert */}
        {(verification.issues.length > 0 || isPolicyReconfirmationNeeded) && (
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
                : isPolicyReconfirmationNeeded && !verification.hasPriceChanges
                ? 'Politika Güncellemesi'
                : 'Fiyat Bilgilendirmesi'}
            </div>
            <div>
              {verification.hasBlockingIssues
                ? 'Sepetinizdeki bazı ürünler veya seçenekler güncellendi/tükendi. Lütfen sipariş vermeden önce ilgili satırları düzenleyin.'
                : isPolicyReconfirmationNeeded && !verification.hasPriceChanges
                ? 'Restoran vergi veya servis ücreti politikası güncellendi. Devam etmek için yeni koşulları onaylamanız gerekmektedir.'
                : 'Sepetinizdeki bazı ürünlerin fiyatları güncellendi. Devam etmek için yeni fiyatları onaylamanız gerekmektedir.'}
            </div>
            {isPolicyReconfirmationNeeded && !verification.hasBlockingIssues && runtimeMenu && (
              <div
                style={{
                  marginTop: 'var(--ro-space-2)',
                  paddingTop: 'var(--ro-space-2)',
                  borderTop: '1px dashed var(--ro-color-border, #e5e7eb)',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '4px',
                }}
                data-testid="policy-change-breakdown"
              >
                <div>
                  <strong>KDV:</strong>{' '}
                  {runtimeMenu.defaultTaxRateBps > 0 ? `%${runtimeMenu.defaultTaxRateBps / 100}` : '%0'}{' '}
                  ({runtimeMenu.pricesIncludeTax ? 'Fiyatlara Dahil' : 'Fiyatlara Hariç'})
                </div>
                {runtimeMenu.isServiceChargeEnabled && runtimeMenu.serviceChargeRateBps > 0 && (
                  <div>
                    <strong>Servis Ücreti:</strong> %{runtimeMenu.serviceChargeRateBps / 100}
                  </div>
                )}
                {currentFinancials && (
                  <div>
                    <strong>Güncel Tahmini Toplam:</strong>{' '}
                    {formatCurrency(currentFinancials.grandTotalMinorUnits, currency)}
                  </div>
                )}
              </div>
            )}
            {verification.hasPriceChanges && (
              <div style={{ marginTop: 'var(--ro-space-2)' }}>
                {verification.issues
                  .filter((i) => i.type === 'price_changed')
                  .map((i) => (
                    <div key={i.lineSignature} style={{ marginBottom: '4px' }}>
                      {i.message}{' '}
                      <strong>
                        {i.previousUnitPriceMinorUnits !== undefined &&
                          formatCurrency(i.previousUnitPriceMinorUnits, currency)}{' '}
                        ➔{' '}
                        {i.currentUnitPriceMinorUnits !== undefined &&
                          formatCurrency(i.currentUnitPriceMinorUnits, currency)}
                      </strong>
                    </div>
                  ))}
              </div>
            )}
            {(verification.hasPriceChanges || isPolicyReconfirmationNeeded) && canAcceptChanges && (
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={handleAcceptPriceChanges}
                data-testid="accept-price-changes-btn"
                style={{ marginTop: 'var(--ro-space-2)', minHeight: '36px' }}
              >
                {isPolicyReconfirmationNeeded && !verification.hasPriceChanges
                  ? 'Yeni Koşulları Kabul Et'
                  : 'Yeni Fiyatları Kabul Et'}
              </Button>
            )}
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

            {/* Order Confirmation CTA */}
            <Button
              type="button"
              variant="primary"
              size="lg"
              disabled={isOrderBlocked}
              aria-disabled={isOrderBlocked}
              onClick={() => {
                onProceedToConfirmation?.();
              }}
              data-testid="submit-order-cta-btn"
              style={{
                minHeight: '48px',
                width: '100%',
                opacity: isOrderBlocked ? 0.6 : 1,
                cursor: isOrderBlocked ? 'not-allowed' : 'pointer',
              }}
            >
              Siparişi Onayla • {formatCurrency(subtotalMinorUnits, currency)}
            </Button>
          </div>
        )}
      </div>
    </BottomSheet>
  );
};
