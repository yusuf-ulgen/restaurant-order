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
}

export const CategoryList: React.FC<Props> = ({ categories, selectedCategoryId, canManage, onSelect, onEdit, onCreate, onToggle }) => {
  const [pending, setPending] = useState<CategoryContract | null>(null);
  const [busy, setBusy] = useState(false);
  return <section aria-labelledby="catalog-category-title">
    <div className="catalog-section-heading"><h2 id="catalog-category-title">Kategoriler</h2>{canManage && <Button size="md" onClick={onCreate}>Kategori ekle</Button>}</div>
    {categories.length === 0 ? <p role="status">Bu menüde henüz kategori yok.</p> : <div className="catalog-list" role="list">
      {categories.map((category) => <div key={category.id} className="catalog-row" role="listitem">
        <button className="catalog-list-button" aria-pressed={selectedCategoryId === category.id} onClick={() => onSelect(category.id)}>
          <span className="catalog-wrap">{category.name}</span>{!category.isActive && <span> · Gizli</span>}
        </button>
        {canManage && <div className="catalog-actions">
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
