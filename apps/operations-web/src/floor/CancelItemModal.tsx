import React, { useState } from 'react';
import {
  StaffOrderItemDto,
  StaffOrderResponseDto,
  CancelOrderItemRequest,
} from '@restaurant-order/contracts';
import { cancelStaffOrderItem, ApiError } from './api';
import { TerminalContextValue } from '../auth/TerminalContext';

export interface CancelItemModalProps {
  isOpen: boolean;
  branchId: string;
  sessionId: string;
  order: StaffOrderResponseDto;
  item: StaffOrderItemDto;
  terminalContext: TerminalContextValue;
  onClose: () => void;
  onItemCancelled: (updatedOrder: StaffOrderResponseDto) => void;
}

const COMMON_REASONS = [
  'Müşteri vazgeçti',
  'Hatalı sipariş girişi',
  'Alerjen / Özel istek değişikliği',
  'Masa değişikliği',
  'Hazırlanma süresi gecikti',
];

export const CancelItemModal: React.FC<CancelItemModalProps> = ({
  isOpen,
  branchId,
  sessionId,
  order,
  item,
  terminalContext,
  onClose,
  onItemCancelled,
}) => {
  const currentRole = terminalContext.staffUser?.role;
  const isDirectManager = currentRole === 'BranchManager' || currentRole === 'RestaurantAdmin';

  const isPrePrep = item.status === 'Submitted';
  const requiresSupervisor = !isPrePrep && !isDirectManager;

  const [selectedReason, setSelectedReason] = useState<string>(COMMON_REASONS[0] || 'Müşteri vazgeçti');
  const [customReason, setCustomReason] = useState<string>('');
  const [supervisorUserId, setSupervisorUserId] = useState<string>('');
  const [supervisorPin, setSupervisorPin] = useState<string>('');

  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  if (!isOpen) return null;

  const finalReason = customReason.trim() ? customReason.trim() : selectedReason;

  const handleCancel = async () => {
    if (!finalReason) {
      setErrorMessage('Lütfen bir iptal gerekçesi belirtin.');
      return;
    }

    if (requiresSupervisor) {
      if (!supervisorUserId.trim()) {
        setErrorMessage('Lütfen yönetici (Supervisor) kullanıcı kimliğini girin.');
        return;
      }
      if (supervisorPin.length !== 4) {
        setErrorMessage('Lütfen 4 haneli yönetici PIN kodunu girin.');
        return;
      }
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const payload: CancelOrderItemRequest = {
        reason: finalReason,
        concurrencyToken: order.concurrencyToken,
      };

      if (requiresSupervisor) {
        payload.supervisorUserId = supervisorUserId.trim();
        payload.supervisorPin = supervisorPin.trim();
      }

      const updatedOrder = await cancelStaffOrderItem(
        branchId,
        sessionId,
        order.id,
        item.id,
        payload,
        order.concurrencyToken
      );

      // PIN memory'den anında temizlenir (güvenlik kuralı)
      setSupervisorPin('');
      setSupervisorUserId('');

      onItemCancelled(updatedOrder);
      onClose();
    } catch (err: unknown) {
      const apiErr = err as ApiError;
      if (apiErr.status === 409) {
        setErrorMessage(
          'Sipariş durumu başka bir işlem tarafından güncellenmiş (Hazırlığa alınmış veya değiştirilmiş olabilir). Lütfen ekranı yenileyiniz.'
        );
      } else if (apiErr.status === 403) {
        setErrorMessage(
          requiresSupervisor
            ? 'Yönetici doğrulaması başarısız: Geçersiz PIN veya yetkisiz yönetici kimliği.'
            : 'Bu kalemi iptal etme yetkiniz bulunmamaktadır.'
        );
      } else {
        setErrorMessage(apiErr.detail || apiErr.message || 'İptal işlemi sırasında hata oluştu.');
      }
      // Hata durumunda da PIN temizlenir
      setSupervisorPin('');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-labelledby="cancel-item-modal-title"
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(0,0,0,0.6)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1200,
        padding: '16px',
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--ro-color-surface, #ffffff)',
          color: 'var(--ro-color-text, #111827)',
          width: '100%',
          maxWidth: '480px',
          borderRadius: '12px',
          boxShadow: '0 8px 30px rgba(0,0,0,0.2)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        {/* Header */}
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
            <h3 id="cancel-item-modal-title" style={{ margin: 0, fontSize: '17px', fontWeight: 600 }}>
              {isPrePrep ? 'Kalem İptali (Hazırlık Öncesi)' : 'Yönetici Kalem İptali (Supervisor Void)'}
            </h3>
            <span style={{ fontSize: '13px', color: 'var(--ro-color-text-muted, #6b7280)' }}>
              {item.quantity}x {item.itemName} (Durum: {item.status})
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Kapat"
            style={{
              background: 'transparent',
              border: 'none',
              fontSize: '20px',
              cursor: 'pointer',
              color: 'var(--ro-color-text-muted, #6b7280)',
              padding: '4px 8px',
            }}
          >
            ✕
          </button>
        </div>

        {/* Content */}
        <div style={{ padding: '20px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
          {/* Reason selection */}
          <div>
            <label htmlFor="cancel-reason-select" style={{ display: 'block', fontWeight: 600, fontSize: '13px', marginBottom: '6px' }}>
              İptal Gerekçesi:
            </label>
            <select
              id="cancel-reason-select"
              value={selectedReason}
              onChange={(e) => setSelectedReason(e.target.value)}
              style={{
                width: '100%',
                padding: '8px 12px',
                borderRadius: '6px',
                border: '1px solid var(--ro-color-border, #e5e7eb)',
                fontSize: '13px',
                marginBottom: '8px',
              }}
            >
              {COMMON_REASONS.map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
              <option value="other">Diğer (Özel Gerekçe)...</option>
            </select>

            {selectedReason === 'other' && (
              <input
                type="text"
                placeholder="Özel gerekçe giriniz..."
                value={customReason}
                onChange={(e) => setCustomReason(e.target.value)}
                style={{
                  width: '100%',
                  padding: '8px 12px',
                  borderRadius: '6px',
                  border: '1px solid var(--ro-color-border, #e5e7eb)',
                  fontSize: '13px',
                  boxSizing: 'border-box',
                }}
              />
            )}
          </div>

          {/* Supervisor Authorization Section */}
          {requiresSupervisor && (
            <div
              style={{
                backgroundColor: '#fffbeb',
                border: '1px solid #fde68a',
                borderRadius: '8px',
                padding: '12px 14px',
                display: 'flex',
                flexDirection: 'column',
                gap: '10px',
              }}
            >
              <div style={{ fontSize: '13px', fontWeight: 600, color: '#92400e' }}>
                ⚠️ Kalem hazırlıkta veya hazır durumda olduğu için Yönetici Onayı gereklidir.
              </div>

              <div>
                <label htmlFor="supervisor-user-id" style={{ display: 'block', fontSize: '12px', fontWeight: 500, color: '#78350f', marginBottom: '4px' }}>
                  Yönetici Kullanıcı ID / E-posta:
                </label>
                <input
                  id="supervisor-user-id"
                  type="text"
                  placeholder="Yönetici kullanıcı kimliği"
                  value={supervisorUserId}
                  onChange={(e) => setSupervisorUserId(e.target.value)}
                  style={{
                    width: '100%',
                    padding: '8px 10px',
                    borderRadius: '6px',
                    border: '1px solid #d97706',
                    fontSize: '13px',
                    boxSizing: 'border-box',
                  }}
                />
              </div>

              <div>
                <label htmlFor="supervisor-pin-input" style={{ display: 'block', fontSize: '12px', fontWeight: 500, color: '#78350f', marginBottom: '4px' }}>
                  Yönetici 4 Haneli PIN:
                </label>
                <input
                  id="supervisor-pin-input"
                  type="password"
                  inputMode="numeric"
                  maxLength={4}
                  placeholder="••••"
                  value={supervisorPin}
                  onChange={(e) => setSupervisorPin(e.target.value.replace(/\D/g, '').slice(0, 4))}
                  style={{
                    width: '120px',
                    padding: '8px 12px',
                    borderRadius: '6px',
                    border: '1px solid #d97706',
                    fontSize: '16px',
                    letterSpacing: '4px',
                    textAlign: 'center',
                    boxSizing: 'border-box',
                  }}
                />
              </div>
            </div>
          )}

          {errorMessage && (
            <div
              role="alert"
              style={{
                padding: '10px 12px',
                borderRadius: '6px',
                backgroundColor: '#fef2f2',
                color: 'var(--ro-color-error, #dc2626)',
                fontSize: '13px',
                lineHeight: 1.4,
              }}
            >
              {errorMessage}
            </div>
          )}
        </div>

        {/* Footer */}
        <div
          style={{
            padding: '14px 20px',
            borderTop: '1px solid var(--ro-color-border, #e5e7eb)',
            display: 'flex',
            justifyContent: 'flex-end',
            gap: '10px',
          }}
        >
          <button
            type="button"
            disabled={isSubmitting}
            onClick={onClose}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: '1px solid var(--ro-color-border, #e5e7eb)',
              backgroundColor: 'transparent',
              fontSize: '13px',
              cursor: 'pointer',
            }}
          >
            Vazgeç
          </button>
          <button
            type="button"
            disabled={isSubmitting}
            onClick={handleCancel}
            style={{
              padding: '8px 18px',
              borderRadius: '6px',
              border: 'none',
              backgroundColor: 'var(--ro-color-error, #dc2626)',
              color: '#ffffff',
              fontSize: '13px',
              fontWeight: 600,
              cursor: isSubmitting ? 'not-allowed' : 'pointer',
              opacity: isSubmitting ? 0.7 : 1,
            }}
          >
            {isSubmitting ? 'İptal Ediliyor...' : 'Kalemi İptal Et'}
          </button>
        </div>
      </div>
    </div>
  );
};
