import React, { useState, useEffect, useMemo, useCallback } from 'react';
import {
  DiningSessionDto,
  RuntimeMenuReadModel,
  RuntimeMenuItem,
  CreateStaffOrderRequest,
  StaffOrderItemRequest,
  StaffOrderResponseDto,
} from '@restaurant-order/contracts';
import { fetchRuntimeMenu, createStaffOrder, ApiError } from './api';
import {
  StaffDraftLine,
  StaffOrderDraft,
  loadStaffOrderDraft,
  saveStaffOrderDraft,
  clearStaffOrderDraft,
  calculateStaffOrderPayloadSignature,
  resolveStaffOrderIdempotencyKey,
  calculateStaffOrderFinancials,
} from './staffOrderDraftStorage';
import { StaffOrderItemCustomizer } from './StaffOrderItemCustomizer';
import { StaffOrderCartSummary } from './StaffOrderCartSummary';
import { StaffOrderItemsGrid } from './StaffOrderItemsGrid';
import { TerminalContextValue } from '../auth/TerminalContext';

export interface StaffOrderDrawerProps {
  isOpen: boolean;
  branchId: string;
  session: DiningSessionDto;
  tableNumber: string;
  terminalContext: TerminalContextValue;
  onClose: () => void;
  onOrderCreated: (order: StaffOrderResponseDto) => void;
}

export const StaffOrderDrawer: React.FC<StaffOrderDrawerProps> = ({
  isOpen,
  branchId,
  session,
  tableNumber,
  terminalContext,
  onClose,
  onOrderCreated,
}) => {
  const staffUserId = terminalContext.staffUser?.userId || 'unknown-staff';
  const tenantId = terminalContext.staffUser?.tenantId || 'unknown-tenant';

  const [runtimeMenu, setRuntimeMenu] = useState<RuntimeMenuReadModel | null>(null);
  const [loadingMenu, setLoadingMenu] = useState<boolean>(false);
  const [menuError, setMenuError] = useState<string | null>(null);

  // Draft state
  const [draftLines, setDraftLines] = useState<StaffDraftLine[]>([]);
  const [activeDraft, setActiveDraft] = useState<StaffOrderDraft | null>(null);

  // Customizer state
  const [customizingItem, setCustomizingItem] = useState<RuntimeMenuItem | null>(null);
  const [editingLine, setEditingLine] = useState<StaffDraftLine | null>(null);

  // Submission state
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // Load runtime menu
  const loadMenu = useCallback(async () => {
    setLoadingMenu(true);
    setMenuError(null);
    try {
      const menu = await fetchRuntimeMenu(branchId);
      setRuntimeMenu(menu);
    } catch (err: unknown) {
      const apiErr = err as ApiError;
      setMenuError(apiErr.detail || apiErr.message || 'Menü yüklenirken hata oluştu.');
    } finally {
      setLoadingMenu(false);
    }
  }, [branchId]);

  useEffect(() => {
    if (isOpen) {
      loadMenu();
      const existing = loadStaffOrderDraft(tenantId, branchId, session.id, staffUserId);
      if (existing) {
        setActiveDraft(existing);
        setDraftLines(existing.items || []);
      } else {
        setActiveDraft(null);
        setDraftLines([]);
      }
      setSubmitError(null);
    }
  }, [isOpen, branchId, session.id, tenantId, staffUserId, loadMenu]);

  // Persist draft on changes
  const updateDraft = useCallback(
    (newLines: StaffDraftLine[]) => {
      setDraftLines(newLines);
      if (!runtimeMenu) return;

      const fin = calculateStaffOrderFinancials(newLines, runtimeMenu);
      const signature = calculateStaffOrderPayloadSignature(
        newLines,
        runtimeMenu.pricingPolicyFingerprint,
        fin.grandTotalMinorUnits
      );
      const { idempotencyKey } = resolveStaffOrderIdempotencyKey(activeDraft, signature);

      const draft: StaffOrderDraft = {
        tenantId,
        branchId,
        sessionId: session.id,
        staffUserId,
        items: newLines,
        idempotencyKey,
        lastPayloadSignature: signature,
        isPendingSubmission: false,
        updatedAt: Date.now(),
      };

      setActiveDraft(draft);
      saveStaffOrderDraft(draft);
    },
    [runtimeMenu, activeDraft, tenantId, branchId, session.id, staffUserId]
  );

  const allCategories = useMemo(() => {
    if (!runtimeMenu) return [];
    return runtimeMenu.menus.flatMap((m) => m.categories);
  }, [runtimeMenu]);

  const confirmPendingBreak = (): boolean => {
    if (activeDraft?.isPendingSubmission) {
      return window.confirm(
        'Bekleyen bir sipariş gönderim denemesi bulunmaktadır. Değişiklik yaparsanız bekleyen deneme iptal edilip yeni sipariş olarak hazırlanacaktır. Devam etmek istiyor musunuz?'
      );
    }
    return true;
  };

  const handleOpenCustomizer = (item: RuntimeMenuItem) => {
    if (!confirmPendingBreak()) return;
    setCustomizingItem(item);
    setEditingLine(null);
  };

  const handleEditLine = (line: StaffDraftLine) => {
    if (!confirmPendingBreak()) return;
    if (!runtimeMenu) return;
    for (const m of runtimeMenu.menus) {
      for (const c of m.categories) {
        const found = c.items.find((i) => i.id === line.menuItemId);
        if (found) {
          setCustomizingItem(found);
          setEditingLine(line);
          return;
        }
      }
    }
  };

  const handleSaveLine = (line: StaffDraftLine) => {
    if (editingLine) {
      updateDraft(draftLines.map((l) => (l.id === editingLine.id ? line : l)));
    } else {
      updateDraft([...draftLines, line]);
    }
    setCustomizingItem(null);
    setEditingLine(null);
  };

  const handleRemoveLine = (lineId: string) => {
    if (!confirmPendingBreak()) return;
    updateDraft(draftLines.filter((l) => l.id !== lineId));
  };

  const handleClearCart = () => {
    clearStaffOrderDraft(tenantId, branchId, session.id, staffUserId);
    setActiveDraft(null);
    setDraftLines([]);
    setSubmitError(null);
  };

  // Submit Staff Order
  const handleSubmitOrder = async () => {
    if (!runtimeMenu || draftLines.length === 0 || isSubmitting) return;

    setIsSubmitting(true);
    setSubmitError(null);

    try {
      const fin = calculateStaffOrderFinancials(draftLines, runtimeMenu);
      const signature = calculateStaffOrderPayloadSignature(
        draftLines,
        runtimeMenu.pricingPolicyFingerprint,
        fin.grandTotalMinorUnits
      );

      // Freeze or reuse exact idempotency key and payload for retries
      const { idempotencyKey } = resolveStaffOrderIdempotencyKey(activeDraft, signature);

      const itemsRequest: StaffOrderItemRequest[] = draftLines.map((line) => ({
        menuItemId: line.menuItemId,
        itemVariantId: line.variantId,
        quantity: line.quantity,
        customerNote: line.customerNote,
        clientExpectedUnitPriceMinorUnits: line.unitPriceMinorUnits,
        selectedModifiers: line.selectedModifiers.map((m) => ({
          modifierGroupId: m.modifierGroupId,
          modifierOptionId: m.modifierOptionId,
        })),
      }));

      const payload: CreateStaffOrderRequest = {
        items: itemsRequest,
        clientPricingPolicyFingerprint: runtimeMenu.pricingPolicyFingerprint,
        clientExpectedTotalMinorUnits: fin.grandTotalMinorUnits,
      };

      const pendingDraft: StaffOrderDraft = {
        tenantId,
        branchId,
        sessionId: session.id,
        staffUserId,
        items: draftLines,
        idempotencyKey,
        lastPayloadSignature: signature,
        isPendingSubmission: true,
        pendingPayload: payload,
        updatedAt: Date.now(),
      };
      saveStaffOrderDraft(pendingDraft);
      setActiveDraft(pendingDraft);

      const createdOrder = await createStaffOrder(branchId, session.id, payload, idempotencyKey);

      clearStaffOrderDraft(tenantId, branchId, session.id, staffUserId);
      setActiveDraft(null);
      setDraftLines([]);
      onOrderCreated(createdOrder);
      onClose();
    } catch (err: unknown) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        if (
          apiErr.errorCode === 'ORDER_PRICE_CHANGED' ||
          apiErr.errorCode === 'ORDER_PRICING_POLICY_CHANGED' ||
          apiErr.errorCode === 'ORDER_TOTAL_CHANGED'
        ) {
          setSubmitError(
            apiErr.detail ||
              'Menü fiyatları veya vergi politikası güncellendi. Lütfen güncel tutarları inceleyip tekrar onaylayın.'
          );
          if (activeDraft) {
            const resetDraft: StaffOrderDraft = {
              ...activeDraft,
              isPendingSubmission: false,
              pendingPayload: undefined,
            };
            setActiveDraft(resetDraft);
            saveStaffOrderDraft(resetDraft);
          }
          void loadMenu();
        } else if (apiErr.errorCode === 'ITEM_OUT_OF_STOCK') {
          setSubmitError('Seçilen kalemlerden biri tükendi (86). Lütfen sepeti güncelleyiniz.');
          if (activeDraft) {
            const resetDraft: StaffOrderDraft = {
              ...activeDraft,
              isPendingSubmission: false,
              pendingPayload: undefined,
            };
            setActiveDraft(resetDraft);
            saveStaffOrderDraft(resetDraft);
          }
          void loadMenu();
        } else if (
          apiErr.errorCode === 'SESSION_NOT_ACTIVE' ||
          apiErr.errorCode === 'BILL_REQUESTED'
        ) {
          setSubmitError(
            'Masa oturumu kapandı veya hesap istendi durumunda. Yeni sipariş eklenemez.'
          );
        } else {
          setSubmitError(apiErr.detail || 'Çakışma hatası: Sipariş durumu değişti.');
        }
      } else if (apiErr.status === 403) {
        setSubmitError('Sipariş oluşturma yetkiniz bulunmamaktadır.');
      } else {
        setSubmitError(
          apiErr.detail ||
            apiErr.message ||
            'Sipariş gönderilirken bağlantı hatası oluştu. Aynı sipariş güvenle tekrar denenebilir.'
        );
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div
      role="dialog"
      aria-labelledby="staff-order-drawer-title"
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(0,0,0,0.5)',
        display: 'flex',
        justifyContent: 'flex-end',
        zIndex: 1000,
      }}
    >
      <div
        style={{
          width: '100%',
          maxWidth: '680px',
          height: '100%',
          backgroundColor: 'var(--ro-color-surface, #ffffff)',
          color: 'var(--ro-color-text, #111827)',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: '-4px 0 24px rgba(0,0,0,0.15)',
        }}
      >
        {/* Drawer Header */}
        <div
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--ro-color-border, #e5e7eb)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <div>
            <h2 id="staff-order-drawer-title" style={{ margin: 0, fontSize: '18px', fontWeight: 600 }}>
              Masa {tableNumber} — Hızlı Sipariş
            </h2>
            <div style={{ fontSize: '12px', color: 'var(--ro-color-text-muted, #6b7280)' }}>
              Oturum: {session.id.slice(0, 8)}... | Personel: {terminalContext.staffUser?.email || 'Personel'}
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Kapat"
            style={{
              background: 'transparent',
              border: 'none',
              fontSize: '22px',
              cursor: 'pointer',
              color: 'var(--ro-color-text-muted, #6b7280)',
              padding: '4px 8px',
            }}
          >
            ✕
          </button>
        </div>

        {/* Menu Items Grid */}
        <StaffOrderItemsGrid
          categories={allCategories}
          loading={loadingMenu}
          error={menuError}
          onRetry={loadMenu}
          onSelectItem={handleOpenCustomizer}
        />

        {/* Cart / Draft Summary Panel */}
        {runtimeMenu && (
          <StaffOrderCartSummary
            lines={draftLines}
            runtimeMenu={runtimeMenu}
            onEditLine={handleEditLine}
            onRemoveLine={handleRemoveLine}
            onClearCart={handleClearCart}
            onSubmitOrder={handleSubmitOrder}
            isSubmitting={isSubmitting}
            errorMessage={submitError}
          />
        )}

        {/* Item Customizer Modal */}
        {customizingItem && runtimeMenu && (
          <StaffOrderItemCustomizer
            item={customizingItem}
            runtimeMenu={runtimeMenu}
            initialLine={editingLine}
            onSave={handleSaveLine}
            onCancel={() => {
              setCustomizingItem(null);
              setEditingLine(null);
            }}
          />
        )}
      </div>
    </div>
  );
};
