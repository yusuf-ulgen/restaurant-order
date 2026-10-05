import React from 'react';
import { Badge } from '@restaurant-order/ui';
import type { CategoryContract, MenuContract, MenuItemContract } from '@restaurant-order/contracts';
import { CatalogSheet } from './CatalogSheet';
import { isSafeCatalogImageUrl } from './catalogValidation';
import { formatCurrency } from './currencyUtils';

interface Props {
  isOpen: boolean;
  menu: MenuContract | null;
  categories: CategoryContract[];
  items: MenuItemContract[];
  currency?: string;
  onClose: () => void;
}

export const CatalogPreviewSheet: React.FC<Props> = ({ isOpen, menu, categories, items, currency = 'TRY', onClose }) => (
  <CatalogSheet isOpen={isOpen} title="Katalog önizlemesi" onClose={onClose}>
    {!menu ? <p>Önizlenecek bir menü seçin.</p> : <div className="catalog-list">
      <header className="catalog-panel"><h2>{menu.name}</h2><p>{menu.description}</p><Badge variant="neutral">Güvenli yönetim önizlemesi</Badge></header>
      {categories.filter((category) => category.isActive).map((category) => <section className="catalog-panel" key={category.id}>
        <h3>{category.name}</h3><p>{category.description}</p>
        {items.filter((item) => item.categoryId === category.id && item.isActive).map((item) => <article className="catalog-row" key={item.id}>
          <div className="catalog-wrap"><strong>{item.name}</strong><p>{item.shortDescription}</p><p>{item.fullDescription}</p>
            {item.imageUrl && isSafeCatalogImageUrl(item.imageUrl) && <img src={item.imageUrl} alt="" loading="lazy" style={{ maxWidth: '100%', maxHeight: 180, objectFit: 'cover' }} />}
            <div className="catalog-tag-list">{item.dietaryTags.map((tag) => <Badge key={tag} variant="neutral">{tag}</Badge>)}{item.allergenTags.map((tag) => <Badge key={tag} variant="warning">Alerjen: {tag}</Badge>)}</div>
            <strong>{formatCurrency(item.basePriceMinorUnits, currency)}</strong>
            {(item.variants ?? []).filter((variant) => variant.isActive).map((variant) => <p key={variant.id}>{variant.name}: {formatCurrency(variant.absolutePriceMinorUnits, currency)}</p>)}
          </div>
        </article>)}
      </section>)}
      {categories.length === 0 && <p role="status">Önizlemede gösterilecek aktif kategori yok.</p>}
    </div>}
  </CatalogSheet>
);
