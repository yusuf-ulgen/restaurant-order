import React, { useState, useRef, useEffect, useMemo } from 'react';
import {
  calculateCartFinancialSummary,
  computeOrderPayloadSignature,
  type RuntimeMenuReadModel,
  type CustomerOrderResponseDto,
  type CreateCustomerOrderRequest,
  type CustomerOrderItemRequest,
  type CustomerSelectedModifierRequest,
} from '@restaurant-order/contracts';
import { BottomSheet, Button } from '@restaurant-order/ui';
import { useCart } from '../../cart/CartContext';
import { formatCurrency } from '../../utils/menuUtils';
import { customerOrderApi, CustomerOrderApiError } from '../../api/customerOrderApi';
import { resolveCartItemDisplay } from './CartItemRow';
import { findModifierGroupId } from '../../utils/customizationUtils';
import { getOrCreateOrderAttempt } from '../../cart/orderAttemptStorage';

export interface OrderConfirmationSheetProps {
  isOpen: boolean;
  onClose: () => void;
  tableInfo?: { tableNumber: string; tableName: string };
  runtimeMenu: RuntimeMenuReadModel | null;
  onOrderSuccess: (order: CustomerOrderResponseDto) => void;
  onSessionExpired?: () => void;
  onResolvePriceConflict?: () => void;
}

export const OrderConfirmationSheet: React.FC<OrderConfirmationSheetProps> = ({
  isOpen,
  onClose,
  tableInfo,
  runtimeMenu,
  onOrderSuccess,
  onSessionExpired,
  onResolvePriceConflict,
}) => {
  const { sessionId, lines, currency, clearCart, acceptedPolicyFingerprint, requiresReconfirmation, requireReconfirmation } = useCart();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [affectedItemId, setAffectedItemId] = useState<string | null>(null);

  const effectiveFingerprint = acceptedPolicyFingerprint || runtimeMenu?.pricingPolicyFingerprint || '';
  const isReconfirmationNeeded =
    !runtimeMenu?.pricingPolicyFingerprint ||
    (acceptedPolicyFingerprint !== null && acceptedPolicyFingerprint !== runtimeMenu.pricingPolicyFingerprint) ||
    Boolean(requiresReconfirmation);

  // Financial summary based on branch pricing policy and live runtime menu
  const pricingPolicy = useMemo(() => ({
    pricesIncludeTax: runtimeMenu?.pricesIncludeTax ?? true,
    defaultTaxRateBps: runtimeMenu?.defaultTaxRateBps ?? 0,
    isServiceChargeEnabled: runtimeMenu?.isServiceChargeEnabled ?? false,
    serviceChargeRateBps: runtimeMenu?.serviceChargeRateBps ?? 0,
  }), [runtimeMenu]);

  const financials = useMemo(() => calculateCartFinancialSummary(lines, pricingPolicy), [lines, pricingPolicy]);

  const itemsForSignatureAndRequest: CustomerOrderItemRequest[] = useMemo(() => {
    return lines.map((line) => {
      const selectedMods: CustomerSelectedModifierRequest[] = [];
      line.selectedModifierOptionIds.forEach((optId) => {
        const grpId = findModifierGroupId(optId, line.menuItemId, runtimeMenu) || optId;
        selectedMods.push({
          modifierGroupId: grpId,
          modifierOptionId: optId,
        });
      });

      return {
        menuItemId: line.menuItemId,
        itemVariantId: line.variantId || null,
        quantity: line.quantity,
        customerNote: line.customerNote || null,
        selectedModifiers: selectedMods.length > 0 ? selectedMods : null,
        clientExpectedUnitPriceMinorUnits: line.unitPriceMinorUnits,
      };
    });
  }, [lines, runtimeMenu]);

  // Idempotency key tracking: stable across retries of the SAME payload
  const payloadSignature = useMemo(() => {
    const signatureItems = itemsForSignatureAndRequest.map((item) => ({
      menuItemId: item.menuItemId,
      itemVariantId: item.itemVariantId,
      quantity: item.quantity,
      customerNote: item.customerNote,
      clientExpectedUnitPriceMinorUnits: item.clientExpectedUnitPriceMinorUnits ?? 0,
      selectedModifiers: item.selectedModifiers || [],
    }));

    return computeOrderPayloadSignature({
      clientPricingPolicyFingerprint: effectiveFingerprint,
      clientExpectedTotalMinorUnits: financials.grandTotalMinorUnits,
      items: signatureItems,
    });
  }, [itemsForSignatureAndRequest, effectiveFingerprint, financials.grandTotalMinorUnits]);

  const alertRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (errorMessage && alertRef.current) {
      alertRef.current.focus();
    }
  }, [errorMessage]);

  const handleSubmitOrder = async () => {
    if (isSubmitting || lines.length === 0) return;

    if (!runtimeMenu?.pricingPolicyFingerprint) {
      setErrorMessage('Menü fiyatlandırma politikası doğrulanamadı. Lütfen menüyü yenileyiniz.');
      return;
    }

    if (isReconfirmationNeeded) {
      setErrorMessage('Fiyatlandırma koşullarında değişiklik tespit edildi. Lütfen sepetinize dönerek yeni koşulları onaylayınız.');
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);
    setErrorCode(null);
    setAffectedItemId(null);

    const request: CreateCustomerOrderRequest = {
      items: itemsForSignatureAndRequest,
      clientPricingPolicyFingerprint: effectiveFingerprint,
      clientExpectedTotalMinorUnits: financials.grandTotalMinorUnits,
    };

    try {
      const idempotencyKey = getOrCreateOrderAttempt(sessionId, payloadSignature);
      const order = await customerOrderApi.submitOrder(request, idempotencyKey);
      clearCart();
      setIsSubmitting(false);
      onClose();
      onOrderSuccess(order);
    } catch (err: unknown) {
      setIsSubmitting(false);
      if (err instanceof CustomerOrderApiError) {
        setErrorMessage(err.userMessage);
        setErrorCode(err.errorCode);
        if (err.affectedMenuItemId) {
          setAffectedItemId(err.affectedMenuItemId);
        }
        if (
          err.errorCode === 'ORDER_PRICE_CHANGED' ||
          err.errorCode === 'ORDER_PRICING_POLICY_CHANGED' ||
          err.errorCode === 'ORDER_TOTAL_CHANGED'
        ) {
          requireReconfirmation(err.errorCode);
        }
        if (err.errorCode === 'SESSION_NOT_ACTIVE') {
          setTimeout(() => {
            onSessionExpired?.();
          }, 2500);
        }
      } else {
        setErrorMessage('Sipariş iletilemedi. Lütfen bağlantınızı kontrol edip tekrar deneyiniz.');
      }
    }
  };

  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={() => {
        if (!isSubmitting) onClose();
      }}
      title={
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
          <span>Siparişi Onayla</span>
        </div>
      }
      description={
        tableInfo
          ? `Masa ${tableInfo.tableNumber} (${tableInfo.tableName}) • Son Kontrol`
          : 'Lütfen sipariş detaylarınızı onaylayın'
      }
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          maxHeight: '75vh',
          boxSizing: 'border-box',
        }}
        data-testid="order-confirmation-sheet"
      >
        {/* Error Alert with Accessibility role="alert" */}
        {errorMessage && (
          <div
            ref={alertRef}
            tabIndex={-1}
            role="alert"
            style={{
              padding: 'var(--ro-space-3)',
              backgroundColor: 'var(--ro-color-error-subtle, #fef2f2)',
              color: 'var(--ro-color-error-text, #991b1b)',
              borderRadius: 'var(--ro-radius-md)',
              marginBottom: 'var(--ro-space-3)',
              fontSize: 'var(--ro-font-size-sm)',
              outline: 'none',
              border: '1px solid var(--ro-color-error-border, #fecaca)',
            }}
            data-testid="order-submit-error-alert"
          >
            <div style={{ fontWeight: 'var(--ro-font-weight-bold)', marginBottom: '2px' }}>
              {errorCode === 'ORDER_PRICE_CHANGED'
                ? 'Fiyat Değişikliği'
                : errorCode === 'ORDER_PRICING_POLICY_CHANGED'
                ? 'Politika Değişikliği'
                : errorCode === 'ORDER_TOTAL_CHANGED'
                ? 'Toplam Değişikliği'
                : errorCode === 'ITEM_OUT_OF_STOCK'
                ? 'Ürün Tükendi'
                : errorCode === 'ORDER_TAKING_DISABLED'
                ? 'Sipariş Alımı Kapalı'
                : 'Sipariş Gönderilemedi'}
            </div>
            <div>{errorMessage}</div>
            {(errorCode === 'ORDER_PRICE_CHANGED' ||
              errorCode === 'ORDER_PRICING_POLICY_CHANGED' ||
              errorCode === 'ORDER_TOTAL_CHANGED') && (
              <div style={{ marginTop: 'var(--ro-space-2)' }}>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => {
                    onClose();
                    onResolvePriceConflict?.();
                  }}
                  data-testid="resolve-price-change-btn"
                >
                  Sepete Dön ve Fiyatları Güncelle
                </Button>
              </div>
            )}
          </div>
        )}

        {/* Order Items Review List */}
        <div
          style={{
            flex: 1,
            overflowY: 'auto',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-3)',
            paddingRight: 'var(--ro-space-1)',
            marginBottom: 'var(--ro-space-4)',
          }}
          data-testid="order-confirmation-items-list"
        >
          {lines.map((line) => {
            const display = resolveCartItemDisplay(line, runtimeMenu);
            const isItemAffected = affectedItemId === line.menuItemId;
            return (
              <div
                key={line.configurationSignature}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'flex-start',
                  padding: 'var(--ro-space-3)',
                  backgroundColor: isItemAffected
                    ? 'var(--ro-color-error-subtle, #fef2f2)'
                    : 'var(--ro-color-surface-subtle, #f9fafb)',
                  borderRadius: 'var(--ro-radius-md)',
                  border: isItemAffected
                    ? '1px solid var(--ro-color-error-border, #fca5a5)'
                    : '1px solid var(--ro-color-border)',
                }}
                data-testid={`confirmation-item-${line.menuItemId}`}
              >
                <div style={{ flex: 1, paddingRight: 'var(--ro-space-3)' }}>
                  <div
                    style={{
                      fontWeight: 'var(--ro-font-weight-medium)',
                      fontSize: 'var(--ro-font-size-sm)',
                      color: 'var(--ro-color-text-primary)',
                    }}
                  >
                    {line.quantity}x {display.itemName}
                  </div>

                  {display.variantName && (
                    <div
                      style={{
                        fontSize: 'var(--ro-font-size-xs)',
                        color: 'var(--ro-color-text-secondary)',
                        marginTop: '2px',
                      }}
                    >
                      Porsiyon: {display.variantName}
                    </div>
                  )}

                  {display.modifierOptionNames.length > 0 && (
                    <div
                      style={{
                        fontSize: 'var(--ro-font-size-xs)',
                        color: 'var(--ro-color-text-muted)',
                        marginTop: '2px',
                      }}
                    >
                      {display.modifierOptionNames.join(', ')}
                    </div>
                  )}

                  {line.customerNote && (
                    <div
                      style={{
                        fontSize: 'var(--ro-font-size-xs)',
                        fontStyle: 'italic',
                        color: 'var(--ro-color-text-muted)',
                        marginTop: '2px',
                      }}
                    >
                      Not: {line.customerNote}
                    </div>
                  )}
                </div>
                <div
                  style={{
                    fontSize: 'var(--ro-font-size-sm)',
                    fontWeight: 'var(--ro-font-weight-bold)',
                    color: 'var(--ro-color-text-primary)',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {formatCurrency(line.lineTotalMinorUnits, currency)}
                </div>
              </div>
            );
          })}
        </div>
        {/* Financial Summary Breakdown */}
        <div
          style={{
            borderTop: '1px solid var(--ro-color-border)',
            paddingTop: 'var(--ro-space-3)',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--ro-space-2)',
            backgroundColor: 'var(--ro-color-surface)',
          }}
          data-testid="order-financial-breakdown"
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--ro-font-size-sm)' }}>
            <span style={{ color: 'var(--ro-color-text-secondary)' }}>Vergi Öncesi Ara Toplam</span>
            <span
              style={{ color: 'var(--ro-color-text-primary)', fontWeight: 'var(--ro-font-weight-medium)' }}
              data-testid="confirmation-net-subtotal"
            >
              {formatCurrency(financials.netSubtotalMinorUnits, currency)}
            </span>
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
            <span data-testid="confirmation-tax-label">
              KDV {pricingPolicy.defaultTaxRateBps > 0 ? `(%${pricingPolicy.defaultTaxRateBps / 100})` : ''} ({pricingPolicy.pricesIncludeTax ? 'Fiyatlara Dahildir' : 'Fiyatlara Hariçtir'})
            </span>
            <span data-testid="confirmation-tax-amount">
              {formatCurrency(financials.taxMinorUnits, currency)}
            </span>
          </div>

          {(pricingPolicy.isServiceChargeEnabled && (pricingPolicy.serviceChargeRateBps > 0 || financials.serviceChargeMinorUnits > 0)) && (
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
              <span data-testid="confirmation-service-label">
                Servis Ücreti {pricingPolicy.serviceChargeRateBps > 0 ? `(%${pricingPolicy.serviceChargeRateBps / 100})` : ''}
              </span>
              <span data-testid="confirmation-service-charge">
                {formatCurrency(financials.serviceChargeMinorUnits, currency)}
              </span>
            </div>
          )}

          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              borderTop: '1px dashed var(--ro-color-border)',
              paddingTop: 'var(--ro-space-2)',
              marginTop: 'var(--ro-space-1)',
            }}
          >
            <span style={{ fontSize: 'var(--ro-font-size-base)', fontWeight: 'var(--ro-font-weight-bold)' }}>
              Genel Toplam
            </span>
            <span
              style={{
                fontSize: 'var(--ro-font-size-lg)',
                fontWeight: 'var(--ro-font-weight-bold)',
                color: 'var(--ro-color-text-primary)',
              }}
              data-testid="confirmation-total-price"
            >
              {formatCurrency(financials.grandTotalMinorUnits, currency)}
            </span>
          </div>

          {/* Action buttons with in-flight double-click guard */}
          <div
            style={{
              display: 'flex',
              gap: 'var(--ro-space-3)',
              marginTop: 'var(--ro-space-3)',
            }}
          >
            <Button
              type="button"
              variant="secondary"
              size="lg"
              disabled={isSubmitting}
              onClick={onClose}
              data-testid="order-cancel-back-btn"
              style={{ flex: 1, minHeight: '48px' }}
            >
              Geri Dön
            </Button>
            <Button
              type="button"
              variant="primary"
              size="lg"
              disabled={isSubmitting || lines.length === 0 || errorCode === 'ORDER_TAKING_DISABLED'}
              aria-disabled={isSubmitting || lines.length === 0 || errorCode === 'ORDER_TAKING_DISABLED'}
              onClick={handleSubmitOrder}
              data-testid="confirm-and-submit-order-btn"
              style={{ flex: 2, minHeight: '48px' }}
            >
              {isSubmitting ? 'İletiliyor...' : 'Siparişi Gönder'}
            </Button>
          </div>
        </div>
      </div>
    </BottomSheet>
  );
};
