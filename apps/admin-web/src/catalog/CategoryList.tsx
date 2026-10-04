import React, { useState } from 'react';
import { Button, ConfirmationDialog } from '@restaurant-order/ui';
import type { CategoryContract } from '@restaurant-order/contracts';

interface Props {
  categories: CategoryContract[];
  selectedCategoryId: string;
  canManage: boolean;
  onSelect: (id: string) => void;
  onEdit: (category: CategoryContract) => void;
  onCreate: () => void;
  onToggle: (category: CategoryContract) => Promise<void>;
  onReorder?: (reorderedCategories: CategoryContract[]) => Promise<void>;
}

export const CategoryList: React.FC<Props> = ({ categories, selectedCategoryId, canManage, onSelect, onEdit, onCreate, onToggle, onReorder }) => {
  const [pending, setPending] = useState<CategoryContract | null>(null);
  const [busy, setBusy] = useState(false);

  const handleMove = async (index: number, direction: 'up' | 'down') => {
    if (!onReorder) return;
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= categories.length) return;
    const next = [...categories];
    const itemA = next[index];
    const itemB = next[targetIndex];
    if (!itemA || !itemB) return;
    next[index] = itemB;
    next[targetIndex] = itemA;
    setBusy(true);
    try {
      await onReorder(next);
    } finally {
      setBusy(false);
    }
  };

  return <section aria-labelledby="catalog-category-title">
    <div className="catalog-section-heading"><h2 id="catalog-category-title">Kategoriler</h2>{canManage && <Button size="md" onClick={onCreate}>Kategori ekle</Button>}</div>
    {categories.length === 0 ? <p role="status">Bu menüde henüz kategori yok.</p> : <div className="catalog-list" role="list">
      {categories.map((category, index) => <div key={category.id} className="catalog-row" role="listitem">
        <button className="catalog-list-button" aria-pressed={selectedCategoryId === category.id} onClick={() => onSelect(category.id)}>
          <span className="catalog-wrap">{category.name}</span>{!category.isActive && <span> · Gizli</span>}
        </button>
        {canManage && <div className="catalog-actions">
          {onReorder && categories.length > 1 && <>
            <Button size="sm" variant="outline" disabled={busy || index === 0} onClick={() => void handleMove(index, 'up')} aria-label={`${category.name} kategorisini yukarı taşı`}>↑</Button>
            <Button size="sm" variant="outline" disabled={busy || index === categories.length - 1} onClick={() => void handleMove(index, 'down')} aria-label={`${category.name} kategorisini aşağı taşı`}>↓</Button>
          </>}
          <Button size="md" variant="outline" onClick={() => onEdit(category)}>Düzenle</Button>
          <Button size="md" variant="outline" onClick={() => setPending(category)}>{category.isActive ? 'Gizle' : 'Göster'}</Button>
        </div>}
      </div>)}
    </div>}
    <ConfirmationDialog isOpen={!!pending} title={pending?.isActive ? 'Kategoriyi gizle' : 'Kategoriyi göster'}
      message={pending?.isActive ? 'Kategori müşterilerin aktif menüsünde görünmez olacak.' : 'Kategori yeniden aktif menüde gösterilecek.'}
      confirmLabel={pending?.isActive ? 'Gizle' : 'Göster'} isLoading={busy}
      onCancel={() => setPending(null)} onConfirm={async () => {
        if (!pending) return;
        setBusy(true);
        try { await onToggle(pending); setPending(null); } catch { /* Parent renders the API error. */ } finally { setBusy(false); }
      }} />
  </section>;
};
