import React, { useState } from 'react';
import { Button, ConfirmationDialog } from '@restaurant-order/ui';
import type { AvailabilityContract, MenuItemContract } from '@restaurant-order/contracts';
import { catalogApi } from './catalogApi';

interface Props { item: MenuItemContract; availability?: AvailabilityContract; canQuick86: boolean; onChanged: () => Promise<void>; onError: (error: unknown) => void }

export const ItemAvailabilityControl: React.FC<Props> = ({ item, availability, canQuick86, onChanged, onError }) => {
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const available = availability?.isAvailable ?? true;
  if (!canQuick86) return <span aria-label="Stok durumu">{available ? 'Stokta' : 'Stokta yok'}</span>;
  const change = async (next: boolean) => {
    setBusy(true);
    try {
      await catalogApi.setAvailability(item.branchId, item.menuId, item, availability, next);
      setConfirm(false);
      await onChanged();
    } catch (error) { onError(error); }
    finally { setBusy(false); }
  };
  return <>
    <Button size="md" variant={available ? 'danger' : 'outline'} onClick={() => available ? setConfirm(true) : void change(true)} loading={busy}>
      {available ? '86 · Stokta yok' : 'Stok yenilendi'}
    </Button>
    <ConfirmationDialog isOpen={confirm} title="Ürünü stokta yok olarak işaretle" message={`${item.name} müşteri menüsünde kullanılamayacak.`}
      confirmLabel="Stokta yok olarak işaretle" isLoading={busy} onCancel={() => setConfirm(false)} onConfirm={() => void change(false)} />
  </>;
};
