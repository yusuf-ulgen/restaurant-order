import React from 'react';
import { Button, Badge, ConfirmationDialog } from '@restaurant-order/ui';
import type { MenuItemContract, AvailabilityContract } from '@restaurant-order/contracts';
import { ItemAvailabilityControl } from './ItemAvailabilityControl';
import { formatCurrency } from './currencyUtils';

interface Props {
  items: MenuItemContract[];
  availability: AvailabilityContract[];
  currency?: string;
  canManage: boolean;
  canPrice: boolean;
  canQuick86: boolean;
  onEdit: (item: MenuItemContract) => void;
  onCreate: () => void;
  onToggleStatus: (item: MenuItemContract) => Promise<void>;
  onAvailabilityChanged: () => Promise<void>;
  onReorder?: (items: MenuItemContract[]) => Promise<void>;
  onError: (error: unknown) => void;
}

export const MenuItemList: React.FC<Props> = ({
  items,
  availability,
  currency = 'TRY',
  canManage,
  canPrice,
  canQuick86,
  onEdit,
  onCreate,
  onToggleStatus,
  onAvailabilityChanged,
  onReorder,
  onError,
}) => {
  const [pending, setPending] = React.useState<MenuItemContract | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [reordering, setReordering] = React.useState(false);

  const handleMove = async (index: number, offset: number) => {
    if (!onReorder || reordering) return;
    const targetIndex = index + offset;
    if (targetIndex < 0 || targetIndex >= items.length) return;
    const reordered = [...items];
    const [moved] = reordered.splice(index, 1);
    if (!moved) return;
    reordered.splice(targetIndex, 0, moved);
    setReordering(true);
    try {
      await onReorder(reordered);
    } catch (err) {
      onError(err);
    } finally {
      setReordering(false);
    }
  };

  return <section aria-labelledby="catalog-item-title">
    <div className="catalog-section-heading">
      <h2 id="catalog-item-title">Ürünler</h2>
      {canManage && <Button size="md" onClick={onCreate} disabled={!canPrice} aria-describedby={!canPrice ? 'catalog-price-permission' : undefined}>Ürün ekle</Button>}
    </div>
    {!canPrice && canManage && <p id="catalog-price-permission">Ürün oluşturmak için ayrıca fiyat yönetimi izni gerekir.</p>}
    {items.length === 0 ? <p role="status">Bu kategoride henüz ürün yok.</p> : <div>
      {items.map((item, index) => {
        const state = availability.find((entry) => entry.menuItemId === item.id && entry.itemVariantId === null);
        return <article className="catalog-item-card" key={item.id}>
          <div className="catalog-wrap">
            <strong>{item.name}</strong>
            <p>{item.shortDescription}</p>
            <div className="catalog-actions">
              <Badge variant={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Aktif' : 'Gizli'}</Badge>
              <span>{formatCurrency(item.basePriceMinorUnits, currency)}</span>
              {(item.variants?.length ?? 0) > 0 && <span>{item.variants?.length} varyant</span>}
            </div>
          </div>
          <div className="catalog-actions">
            {canManage && onReorder && items.length > 1 && (
              <div className="catalog-actions" role="group" aria-label={`${item.name} sırasını değiştir`}>
                <Button size="sm" variant="outline" aria-label="Yukarı taşı" disabled={index === 0 || reordering} onClick={() => handleMove(index, -1)}>↑</Button>
                <Button size="sm" variant="outline" aria-label="Aşağı taşı" disabled={index === items.length - 1 || reordering} onClick={() => handleMove(index, 1)}>↓</Button>
              </div>
            )}
            {canManage && <>
              <Button size="md" variant="outline" onClick={() => onEdit(item)}>Düzenle</Button>
              <Button size="md" variant="outline" onClick={() => setPending(item)}>{item.isActive ? 'Gizle' : 'Yayınla'}</Button>
            </>}
            <ItemAvailabilityControl item={item} availability={state} canQuick86={canQuick86} onChanged={onAvailabilityChanged} onError={onError} />
          </div>
        </article>;
      })}
    </div>}
    <ConfirmationDialog isOpen={!!pending} title={pending?.isActive ? 'Ürünü gizle' : 'Ürünü yayınla'}
      message={pending?.isActive ? 'Ürün aktif menüde görünmeyecek.' : 'Ürün müşterilerin aktif menüsünde gösterilecek.'}
      confirmLabel={pending?.isActive ? 'Gizle' : 'Yayınla'} isLoading={busy}
      onCancel={() => setPending(null)} onConfirm={async () => {
        if (!pending) return;
        setBusy(true); try { await onToggleStatus(pending); setPending(null); } catch { /* Parent renders the API error. */ } finally { setBusy(false); }
      }} />
  </section>;
};
