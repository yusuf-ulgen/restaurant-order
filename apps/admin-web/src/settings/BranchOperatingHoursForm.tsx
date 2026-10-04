import React, { useState, useEffect } from 'react';
import {
  Card,
  Button,
  Input,
  Switch,
  Badge,
  FormError,
} from '@restaurant-order/ui';
import {
  BranchOperatingHoursContract,
  OperatingDayScheduleContract,
  UpdateBranchOperatingHoursRequest,
} from '@restaurant-order/contracts';

const DAY_NAMES: { [key: number]: string } = {
  1: 'Pazartesi',
  2: 'Salı',
  3: 'Çarşamba',
  4: 'Perşembe',
  5: 'Cuma',
  6: 'Cumartesi',
  0: 'Pazar',
};

// Display sequence starting Monday to Sunday
const DAY_ORDER = [1, 2, 3, 4, 5, 6, 0];

export interface BranchOperatingHoursFormProps {
  operatingHours: BranchOperatingHoursContract;
  isSubmitting: boolean;
  onSave: (payload: UpdateBranchOperatingHoursRequest) => Promise<void>;
  onReset: () => void;
  onHasUnsavedChanges?: (hasChanges: boolean) => void;
}

export const BranchOperatingHoursForm: React.FC<BranchOperatingHoursFormProps> = ({
  operatingHours,
  isSubmitting,
  onSave,
  onReset,
  onHasUnsavedChanges,
}) => {
  const [days, setDays] = useState<OperatingDayScheduleContract[]>(() =>
    JSON.parse(JSON.stringify(operatingHours.days))
  );
  const [validationError, setValidationError] = useState<string | null>(null);

  useEffect(() => {
    setDays(JSON.parse(JSON.stringify(operatingHours.days)));
    setValidationError(null);
  }, [operatingHours]);

  const hasChanges =
    JSON.stringify(days) !== JSON.stringify(operatingHours.days);

  useEffect(() => {
    onHasUnsavedChanges?.(hasChanges);
  }, [hasChanges, onHasUnsavedChanges]);

  const handleToggleClosed = (dayOfWeek: number, isClosed: boolean) => {
    setDays((prev) =>
      prev.map((d) => {
        if (d.dayOfWeek === dayOfWeek) {
          return {
            ...d,
            isClosed,
            slots: isClosed
              ? []
              : d.slots.length > 0
              ? d.slots
              : [{ openTime: '09:00', closeTime: '22:00' }],
          };
        }
        return d;
      })
    );
  };

  const handleSlotChange = (
    dayOfWeek: number,
    index: number,
    field: 'openTime' | 'closeTime',
    val: string
  ) => {
    setDays((prev) =>
      prev.map((d) => {
        if (d.dayOfWeek === dayOfWeek) {
          const current = d.slots[index];
          if (!current) return d;
          const openTime = field === 'openTime' ? val : current.openTime;
          const closeTime = field === 'closeTime' ? val : current.closeTime;
          const newSlots = [...d.slots];
          newSlots[index] = {
            openTime,
            closeTime,
            isOvernight: closeTime < openTime,
          };
          return { ...d, slots: newSlots };
        }
        return d;
      })
    );
  };

  const handleAddSlot = (dayOfWeek: number) => {
    setDays((prev) =>
      prev.map((d) => {
        if (d.dayOfWeek === dayOfWeek) {
          return {
            ...d,
            slots: [...d.slots, { openTime: '18:00', closeTime: '23:00' }],
          };
        }
        return d;
      })
    );
  };

  const handleRemoveSlot = (dayOfWeek: number, index: number) => {
    setDays((prev) =>
      prev.map((d) => {
        if (d.dayOfWeek === dayOfWeek) {
          const newSlots = d.slots.filter((_, i) => i !== index);
          return {
            ...d,
            slots: newSlots,
            isClosed: newSlots.length === 0 ? true : d.isClosed,
          };
        }
        return d;
      })
    );
  };

  const validate = (): boolean => {
    setValidationError(null);

    const timeRegex = /^([01]\d|2[0-3]):[0-5]\d$/;

    for (const day of days) {
      if (!day.isClosed) {
        if (day.slots.length === 0) {
          setValidationError(`${DAY_NAMES[day.dayOfWeek]} açık olarak işaretlenmiş ancak çalışma saati girilmemiş.`);
          return false;
        }

        for (const slot of day.slots) {
          if (!timeRegex.test(slot.openTime) || !timeRegex.test(slot.closeTime)) {
            setValidationError(`${DAY_NAMES[day.dayOfWeek]} için saatler HH:mm formatında olmalıdır.`);
            return false;
          }
          if (slot.openTime === slot.closeTime) {
            setValidationError(`${DAY_NAMES[day.dayOfWeek]} için başlangıç ve bitiş saati aynı olamaz (${slot.openTime}).`);
            return false;
          }
        }
      } else {
        if (day.slots.length > 0) {
          setValidationError(`${DAY_NAMES[day.dayOfWeek]} kapalı olarak ayarlanmışken saat aralığı içeremez.`);
          return false;
        }
      }
    }

    return true;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validate()) return;

    const payload: UpdateBranchOperatingHoursRequest = {
      days: days.map((d) => ({
        dayOfWeek: d.dayOfWeek,
        isClosed: d.isClosed,
        slots: d.slots.map((s) => ({ openTime: s.openTime, closeTime: s.closeTime })),
      })),
      concurrencyToken: operatingHours.concurrencyToken,
    };

    await onSave(payload);
  };

  const sortedDays = [...days].sort(
    (a, b) => DAY_ORDER.indexOf(a.dayOfWeek) - DAY_ORDER.indexOf(b.dayOfWeek)
  );

  return (
    <form onSubmit={handleSubmit} noValidate data-testid="branch-operating-hours-form">
      {hasChanges && (
        <div style={{ marginBottom: 'var(--ro-space-3)' }}>
          <Badge variant="warning">Kaydedilmemiş çalışma saati değişiklikleri var</Badge>
        </div>
      )}

      {validationError && (
        <div style={{ marginBottom: 'var(--ro-space-4)' }}>
          <FormError id="hours-form-error">{validationError}</FormError>
        </div>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
        {sortedDays.map((day) => {
          const dayName = DAY_NAMES[day.dayOfWeek] || `Gün ${day.dayOfWeek}`;

          return (
            <Card key={day.dayOfWeek} padding="md" data-testid={`day-row-${day.dayOfWeek}`}>
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  flexWrap: 'wrap',
                  gap: 'var(--ro-space-2)',
                  marginBottom: day.isClosed ? 0 : 'var(--ro-space-3)',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-3)' }}>
                  <span style={{ fontWeight: 600, fontSize: 'var(--ro-font-size-md)', minWidth: '100px' }}>
                    {dayName}
                  </span>
                  {day.isClosed ? (
                    <Badge variant="neutral">Kapalı</Badge>
                  ) : (
                    <Badge variant="success">Açık</Badge>
                  )}
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                  <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                    {day.isClosed ? 'Açılışa Al' : 'Tüm Gün Kapat'}
                  </span>
                  <Switch
                    checked={!day.isClosed}
                    data-testid={`switch-open-${day.dayOfWeek}`}
                    onCheckedChange={(isOpen) => handleToggleClosed(day.dayOfWeek, !isOpen)}
                    label={`${dayName} açık`}
                  />
                </div>
              </div>

              {!day.isClosed && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-2)', marginTop: 'var(--ro-space-2)' }}>
                  {day.slots.map((slot, idx) => {
                    const isOvernight = slot.closeTime < slot.openTime;

                    return (
                      <div
                        key={idx}
                        data-testid={`slot-${day.dayOfWeek}-${idx}`}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          gap: 'var(--ro-space-3)',
                          flexWrap: 'wrap',
                          padding: 'var(--ro-space-2)',
                          backgroundColor: 'var(--ro-color-surface-hover)',
                          borderRadius: 'var(--ro-radius-md)',
                        }}
                      >
                        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                          <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                            Açılış:
                          </span>
                          <Input
                            type="time"
                            data-testid={`input-open-${day.dayOfWeek}-${idx}`}
                            value={slot.openTime}
                            onChange={(e) => handleSlotChange(day.dayOfWeek, idx, 'openTime', e.target.value)}
                            style={{ width: '110px' }}
                          />
                        </div>

                        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                          <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                            Kapanış:
                          </span>
                          <Input
                            type="time"
                            data-testid={`input-close-${day.dayOfWeek}-${idx}`}
                            value={slot.closeTime}
                            onChange={(e) => handleSlotChange(day.dayOfWeek, idx, 'closeTime', e.target.value)}
                            style={{ width: '110px' }}
                          />
                        </div>

                        {isOvernight && (
                          <Badge variant="warning" data-testid={`overnight-badge-${day.dayOfWeek}-${idx}`}>
                            Gece Yarısını Geçer (Ertesi Gün)
                          </Badge>
                        )}

                        <div style={{ marginLeft: 'auto' }}>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            data-testid={`btn-remove-slot-${day.dayOfWeek}-${idx}`}
                            onClick={() => handleRemoveSlot(day.dayOfWeek, idx)}
                          >
                            Sil
                          </Button>
                        </div>
                      </div>
                    );
                  })}

                  <div style={{ marginTop: 'var(--ro-space-2)' }}>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      data-testid={`btn-add-slot-${day.dayOfWeek}`}
                      onClick={() => handleAddSlot(day.dayOfWeek)}
                    >
                      + Aralık Ekle
                    </Button>
                  </div>
                </div>
              )}
            </Card>
          );
        })}
      </div>

      <div style={{ display: 'flex', gap: 'var(--ro-space-3)', justifyContent: 'flex-end', marginTop: 'var(--ro-space-4)' }}>
        <Button
          type="button"
          variant="outline"
          onClick={onReset}
          disabled={isSubmitting || !hasChanges}
        >
          Sıfırla
        </Button>
        <Button
          type="submit"
          variant="primary"
          loading={isSubmitting}
          disabled={isSubmitting}
          data-testid="btn-save-hours"
        >
          Çalışma Saatlerini Kaydet
        </Button>
      </div>
    </form>
  );
};
