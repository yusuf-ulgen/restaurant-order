import React, { useState, useEffect, useRef, useCallback } from 'react';
import { BottomSheet, VisuallyHidden } from '@restaurant-order/ui';
import type {
  ServiceRequestDto,
  ServiceRequestType,
  CustomerSessionDto,
} from '@restaurant-order/contracts';
import { customerServiceApi, ServiceApiError } from '../../api/customerServiceApi';
import { ServiceActionCard, type ServiceActionConfig } from './ServiceActionCard';

export interface ServiceRequestSheetProps {
  isOpen: boolean;
  onClose: () => void;
  session: CustomerSessionDto;
  onSessionUpdated?: (updatedSession: CustomerSessionDto) => void;
}

const SERVICE_CONFIGS: ServiceActionConfig[] = [
  {
    key: 'Waiter',
    title: 'Garson Çağır',
    icon: '🙋‍♂️',
    description: 'Masaya görevli servis personeli çağırın.',
    confirmationMessage: 'Masanıza garson çağırmak istediğinizi onaylıyor musunuz?',
    allowNote: true,
  },
  {
    key: 'Water',
    title: 'Su İste',
    icon: '💧',
    description: 'Masanıza su servisi talep edin.',
    confirmationMessage: 'Masanıza su getirilmesini istiyor musunuz?',
    allowNote: true,
  },
  {
    key: 'Napkin',
    title: 'Peçete İste',
    icon: '🧻',
    description: 'Masanıza ilave peçete ve servis gereçleri isteyin.',
    confirmationMessage: 'Masanıza peçete getirilmesini istiyor musunuz?',
    allowNote: true,
  },
  {
    key: 'Assistance',
    title: 'Yardım İste',
    icon: '💬',
    description: 'Menü, alerjenler veya sipariş hakkında soru sorun.',
    confirmationMessage: 'Yardım veya destek talebi iletmek istiyor musunuz?',
    allowNote: true,
  },
  {
    key: 'Bill',
    title: 'Hesap İste',
    icon: '🧾',
    description: 'Masa hesabınızı talep edin.',
    confirmationMessage: 'Hesap istemek üzeresiniz. Onaylıyor musunuz?',
    allowNote: false,
  },
];

export const ServiceRequestSheet: React.FC<ServiceRequestSheetProps> = ({
  isOpen,
  onClose,
  session,
  onSessionUpdated,
}) => {
  const [openRequests, setOpenRequests] = useState<ServiceRequestDto[]>([]);
  const [submittingKey, setSubmittingKey] = useState<string | null>(null);
  const [cancellingId, setCancellingId] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [cooldownSeconds, setCooldownSeconds] = useState(0);
  const [announcement, setAnnouncement] = useState('');

  const isMountedRef = useRef(true);
  const inFlightRef = useRef(false);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  // Cooldown timer interval
  useEffect(() => {
    if (cooldownSeconds <= 0) return;
    const interval = setInterval(() => {
      setCooldownSeconds((prev) => (prev > 1 ? prev - 1 : 0));
    }, 1000);
    return () => clearInterval(interval);
  }, [cooldownSeconds]);

  // Load open requests when sheet opens
  const loadOpenRequests = useCallback(async () => {
    try {
      const data = await customerServiceApi.getOpenRequests();
      if (isMountedRef.current) {
        setOpenRequests(data);
      }
    } catch {
      // Non-blocking initial fetch failure
    }
  }, []);

  useEffect(() => {
    if (isOpen) {
      loadOpenRequests();
    }
  }, [isOpen, loadOpenRequests]);

  const handleConfirmAction = async (key: ServiceRequestType | 'Bill', note?: string) => {
    if (inFlightRef.current || cooldownSeconds > 0) return;

    inFlightRef.current = true;
    setSubmittingKey(key);
    setErrors((prev) => ({ ...prev, [key]: '' }));

    try {
      if (key === 'Bill') {
        const updated = await customerServiceApi.requestBill(
          session.branchId,
          session.sessionId,
          session.concurrencyToken
        );
        if (isMountedRef.current) {
          setAnnouncement('Hesap talebiniz başarıyla personele iletildi.');
          onSessionUpdated?.({
            ...session,
            sessionStatus: updated.status,
            concurrencyToken: updated.concurrencyToken,
          });
        }
      } else {
        const created = await customerServiceApi.createServiceRequest(key, note);
        if (isMountedRef.current) {
          setOpenRequests((prev) => {
            const filtered = prev.filter((r) => r.requestType !== key);
            return [...filtered, created];
          });
          setAnnouncement(`${key} talebiniz başarıyla personele iletildi.`);
        }
      }
    } catch (err: unknown) {
      if (isMountedRef.current) {
        if (err instanceof ServiceApiError) {
          if (err.code === 'rate_limited' && err.retryAfterSeconds) {
            setCooldownSeconds(err.retryAfterSeconds);
          }
          setErrors((prev) => ({ ...prev, [key]: err.message }));
          setAnnouncement(`Hata: ${err.message}`);
        } else {
          const genericMsg = 'Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.';
          setErrors((prev) => ({ ...prev, [key]: genericMsg }));
          setAnnouncement(`Hata: ${genericMsg}`);
        }
      }
    } finally {
      if (isMountedRef.current) {
        setSubmittingKey(null);
      }
      inFlightRef.current = false;
    }
  };

  const handleCancelRequest = async (requestId: string) => {
    if (inFlightRef.current) return;

    inFlightRef.current = true;
    setCancellingId(requestId);

    try {
      await customerServiceApi.cancelRequest(requestId);
      if (isMountedRef.current) {
        setOpenRequests((prev) => prev.filter((r) => r.id !== requestId));
        setAnnouncement('Talebiniz iptal edildi.');
      }
    } catch (err: unknown) {
      if (isMountedRef.current) {
        const msg = err instanceof ServiceApiError ? err.message : 'İptal edilemedi.';
        setAnnouncement(`Hata: ${msg}`);
      }
    } finally {
      if (isMountedRef.current) {
        setCancellingId(null);
      }
      inFlightRef.current = false;
    }
  };

  const isBillRequested = session.sessionStatus === 'BillRequested';

  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={onClose}
      title="Masa Servis Talepleri"
      description="Masanız için servis veya destek talebi iletin."
    >
      <div
        data-testid="service-request-sheet"
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--ro-space-3, 12px)',
          padding: 'var(--ro-space-2, 8px) 0',
        }}
      >
        <VisuallyHidden>
          <div role="status" aria-live="polite" data-testid="service-announcement">
            {announcement}
          </div>
        </VisuallyHidden>

        {SERVICE_CONFIGS.map((config) => {
          const openReq = openRequests.find((r) => r.requestType === config.key);
          const isSubmitting = submittingKey === config.key;
          const isCancelling = openReq ? cancellingId === openReq.id : false;
          const errorMsg = errors[config.key];

          return (
            <ServiceActionCard
              key={config.key}
              config={config}
              openRequest={openReq}
              isBillRequested={isBillRequested}
              isSubmitting={isSubmitting}
              isCancelling={isCancelling}
              cooldownSeconds={cooldownSeconds}
              errorMessage={errorMsg}
              onConfirm={handleConfirmAction}
              onCancelRequest={handleCancelRequest}
              onRetry={() => handleConfirmAction(config.key)}
            />
          );
        })}
      </div>
    </BottomSheet>
  );
};
