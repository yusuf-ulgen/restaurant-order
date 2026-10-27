import React, { useState, useMemo } from 'react';
import {
  Modal,
  Button,
  Select,
  Spinner,
} from '@restaurant-order/ui';
import { TableFloorStatusDto } from '@restaurant-order/contracts';
import { resolveTableOperationalStatus } from './types';
import { transferSession, mergeSession, ApiError } from './api';

export interface SessionMovementModalProps {
  isOpen: boolean;
  onClose: () => void;
  branchId: string;
  sourceTable: TableFloorStatusDto | null;
  allTables: TableFloorStatusDto[];
  onSuccess: () => Promise<void>;
}

export const SessionMovementModal: React.FC<SessionMovementModalProps> = ({
  isOpen,
  onClose,
  branchId,
  sourceTable,
  allTables,
  onSuccess,
}) => {
  const [movementType, setMovementType] = useState<'transfer' | 'merge'>('transfer');
  const [selectedTargetTableId, setSelectedTargetTableId] = useState<string>('');
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isMergeConfirmed, setIsMergeConfirmed] = useState<boolean>(false);

  // Transfer için boş ve aktif hedef masalar
  const availableTargetTables = useMemo(() => {
    return allTables.filter((t) => {
      if (t.table.id === sourceTable?.table.id) return false;
      const status = resolveTableOperationalStatus(t);
      return status === 'Available' && t.table.isActive;
    });
  }, [allTables, sourceTable?.table.id]);

  // Birleştirme için aktif oturumu olan hedef masalar
  const mergeableTargetTables = useMemo(() => {
    return allTables.filter((t) => {
      if (t.table.id === sourceTable?.table.id) return false;
      const status = resolveTableOperationalStatus(t);
      return (
        t.table.isActive &&
        t.activeSession &&
        status !== 'Available' &&
        status !== 'Inactive' &&
        status !== 'BillRequested'
      );
    });
  }, [allTables, sourceTable?.table.id]);

  const targetTableOptions = useMemo(() => {
    const list = movementType === 'transfer' ? availableTargetTables : mergeableTargetTables;
    return [
      { value: '', label: '-- Hedef Masa Seçiniz --' },
      ...list.map((t) => ({
        value: t.table.id,
        label: `${t.table.tableNumber} - ${t.table.name} (Kapasite: ${t.table.capacity})`,
      })),
    ];
  }, [movementType, availableTargetTables, mergeableTargetTables]);

  if (!sourceTable || !sourceTable.activeSession) return null;

  const sourceSession = sourceTable.activeSession;

  const handleSubmit = async () => {
    if (!selectedTargetTableId) {
      setErrorMessage('Lütfen bir hedef masa seçiniz.');
      return;
    }

    setErrorMessage(null);
    setIsSubmitting(true);

    try {
      if (movementType === 'transfer') {
        await transferSession(
          branchId,
          sourceSession.id,
          selectedTargetTableId,
          sourceSession.concurrencyToken
        );
      } else {
        const targetTable = allTables.find((t) => t.table.id === selectedTargetTableId);
        if (!targetTable || !targetTable.activeSession) {
          throw new ApiError('Hedef masada geçerli bir aktif oturum bulunamadı.', 400);
        }

        await mergeSession(
          branchId,
          sourceSession.id,
          targetTable.activeSession.id,
          sourceSession.concurrencyToken,
          targetTable.activeSession.concurrencyToken
        );
      }

      await onSuccess();
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.status === 409) {
          setErrorMessage(
            'Eşzamanlı işlem çakışması tespit edildi. Masanın durumu başka bir işlem tarafından güncellenmiş olabilir. Lütfen güncel durumu kontrol ediniz.'
          );
        } else {
          setErrorMessage(err.message || 'Masa taşıma veya birleştirme işlemi gerçekleştirilemedi.');
        }
      } else {
        setErrorMessage('Beklenmeyen bir hata oluştu.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Masa Taşıma / Birleştirme"
      description={`Kaynak Masa: ${sourceTable.table.tableNumber} - ${sourceTable.table.name}`}
      size="sm"
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        {/* Hata Alanı */}
        {errorMessage && (
          <div
            role="alert"
            style={{
              padding: '10px 12px',
              backgroundColor: '#fee2e2',
              color: '#b91c1c',
              borderRadius: '6px',
              fontSize: '0.9rem',
              border: '1px solid #f87171',
            }}
          >
            ⚠️ {errorMessage}
          </div>
        )}

        {/* İşlem Türü Seçimi */}
        <div style={{ display: 'flex', gap: '8px' }}>
          <Button
            variant={movementType === 'transfer' ? 'primary' : 'outline'}
            size="md"
            onClick={() => {
              setMovementType('transfer');
              setSelectedTargetTableId('');
              setErrorMessage(null);
            }}
            style={{ flex: 1, minHeight: '44px' }}
          >
            📦 Masayı Taşı
          </Button>
          <Button
            variant={movementType === 'merge' ? 'primary' : 'outline'}
            size="md"
            onClick={() => {
              setMovementType('merge');
              setSelectedTargetTableId('');
              setErrorMessage(null);
              setIsMergeConfirmed(false);
            }}
            style={{ flex: 1, minHeight: '44px' }}
          >
            🔀 Masaları Birleştir
          </Button>
        </div>

        {/* Açıklama */}
        <p style={{ margin: 0, fontSize: '0.85rem', color: '#4b5563' }}>
          {movementType === 'transfer'
            ? 'Masa Taşı: Kaynak masadaki açık oturum ve tüm siparişler seçilen boş hedef masaya aktarılır. Kaynak masa boşalır.'
            : 'Masa Birleştir: Kaynak masadaki siparişler seçilen hedef masanın oturumuna dahil edilir. Kaynak oturum kapatılır.'}
        </p>

        {/* Hedef Masa Seçimi */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
          <label htmlFor="target-table-select" style={{ fontSize: '0.85rem', fontWeight: 600 }}>
            {movementType === 'transfer' ? 'Hedef Boş Masa:' : 'Birleştirilecek Hedef Masa:'}
          </label>
          <Select
            id="target-table-select"
            value={selectedTargetTableId}
            onChange={(e) => {
              setSelectedTargetTableId(e.target.value);
              setErrorMessage(null);
            }}
            options={targetTableOptions}
            style={{ minHeight: '44px', width: '100%' }}
          />
          {targetTableOptions.length <= 1 && (
            <span style={{ fontSize: '0.8rem', color: '#ef4444' }}>
              {movementType === 'transfer'
                ? 'Şubede transfer edilebilecek boş masa bulunmuyor.'
                : 'Şubede birleştirilebilecek aktif oturumlu masa bulunmuyor.'}
            </span>
          )}
        </div>

        {/* Birleştirme Onay Kutusu */}
        {movementType === 'merge' && selectedTargetTableId && (
          <div
            style={{
              padding: '10px 12px',
              backgroundColor: '#fffbeb',
              border: '1px solid #fde68a',
              borderRadius: '6px',
            }}
          >
            <label
              style={{
                display: 'flex',
                alignItems: 'flex-start',
                gap: '8px',
                cursor: 'pointer',
                fontSize: '0.85rem',
                color: '#92400e',
              }}
            >
              <input
                type="checkbox"
                checked={isMergeConfirmed}
                onChange={(e) => setIsMergeConfirmed(e.target.checked)}
                style={{ minWidth: '20px', minHeight: '20px', marginTop: '2px' }}
              />
              <span>
                Kaynak masadaki tüm siparişlerin hedef masaya aktarılacağını ve bu işlemin geri alınamayacağını onaylıyorum.
              </span>
            </label>
          </div>
        )}

        {/* Aksiyon Butonları */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '8px' }}>
          <Button variant="outline" size="md" onClick={onClose} disabled={isSubmitting} style={{ minHeight: '44px' }}>
            İptal
          </Button>
          <Button
            variant="primary"
            size="md"
            onClick={handleSubmit}
            disabled={
              isSubmitting ||
              !selectedTargetTableId ||
              (movementType === 'merge' && !isMergeConfirmed)
            }
            style={{ minHeight: '44px' }}
          >
            {isSubmitting ? <Spinner size="sm" /> : movementType === 'transfer' ? 'Taşımayı Tamamla' : 'Birleştirmeyi Onayla'}
          </Button>
        </div>
      </div>
    </Modal>
  );
};
