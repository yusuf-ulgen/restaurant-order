import React, { useEffect, useMemo, useState } from 'react';
import { Button, ConfirmationDialog } from '@restaurant-order/ui';
import type { CategoryContract, MenuItemContract, ModifierGroupContract } from '@restaurant-order/contracts';
import { CatalogSheet } from './CatalogSheet';
import { DietaryAndAllergenFields } from './DietaryAndAllergenFields';
import { ModifierGroupEditor } from './ModifierGroupEditor';
import { VariantEditor } from './VariantEditor';
import { dietaryAllergenConflict, isSafeCatalogImageUrl } from './catalogValidation';

export interface ItemDraft {
  categoryId: string; name: string; slug: string; basePriceMinorUnits: number; shortDescription: string;
  fullDescription: string; imageUrl: string; sortOrder: number; spicyLevel: number; dietaryTags: string[]; allergenTags: string[];
}

interface Props {
  isOpen: boolean; branchId: string; menuId: string; item: MenuItemContract | null; categories: CategoryContract[];
  allItems: MenuItemContract[]; groups: ModifierGroupContract[]; canManage: boolean; canPrice: boolean;
  onClose: () => void; onSave: (draft: ItemDraft, item?: MenuItemContract) => Promise<void>;
  onNestedSaved: () => Promise<void>; onError: (error: unknown) => void;
}

const makeDraft = (item: MenuItemContract | null, categoryId: string, sortOrder: number): ItemDraft => ({
  categoryId: item?.categoryId ?? categoryId, name: item?.name ?? '', slug: item?.slug ?? '',
  basePriceMinorUnits: item?.basePriceMinorUnits ?? 0, shortDescription: item?.shortDescription ?? '',
  fullDescription: item?.fullDescription ?? '', imageUrl: item?.imageUrl ?? '', sortOrder: item?.sortOrder ?? sortOrder,
  spicyLevel: item?.spicyLevel ?? 0, dietaryTags: [...(item?.dietaryTags ?? [])], allergenTags: [...(item?.allergenTags ?? [])],
});

export const MenuItemEditorSheet: React.FC<Props> = ({ isOpen, branchId, menuId, item, categories, allItems, groups, canManage, canPrice, onClose, onSave, onNestedSaved, onError }) => {
  const initial = useMemo(() => makeDraft(item, categories.find((entry) => entry.isActive)?.id ?? '', allItems.length), [item, categories, allItems.length]);
  const [draft, setDraft] = useState(initial);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirmClose, setConfirmClose] = useState(false);
  useEffect(() => { setDraft(initial); setError(''); }, [isOpen, item?.id]);
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial);
  const requestClose = () => dirty ? setConfirmClose(true) : onClose();
  const change = <K extends keyof ItemDraft>(key: K, value: ItemDraft[K]) => setDraft((current) => ({ ...current, [key]: value }));
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const conflict = dietaryAllergenConflict(draft.dietaryTags, draft.allergenTags);
    if (conflict) { setError(conflict); return; }
    if (!draft.name.trim() || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(draft.slug)) { setError('Ürün adı zorunludur; slug küçük harf, sayı ve tire içermelidir.'); return; }
    if (allItems.some((entry) => entry.id !== item?.id && entry.slug === draft.slug)) { setError('Bu slug menüde zaten kullanılıyor.'); return; }
    if (!draft.categoryId) { setError('Bir kategori seçin.'); return; }
    if (!canPrice && !item) { setError('Yeni ürün oluşturmak için fiyat yönetimi izni gerekir.'); return; }
    if (!Number.isSafeInteger(draft.basePriceMinorUnits) || draft.basePriceMinorUnits < 0) { setError('Geçerli bir fiyat girin.'); return; }
    if (!isSafeCatalogImageUrl(draft.imageUrl)) { setError('Görsel adresi güvenli bir site içi yol veya HTTPS adresi olmalıdır.'); return; }
    setBusy(true); setError('');
    try { await onSave(draft, item ?? undefined); onClose(); }
    catch (reason) { onError(reason); setError(reason instanceof Error ? reason.message : 'Ürün kaydedilemedi.'); }
    finally { setBusy(false); }
  };
  const setTags = (key: 'dietaryTags' | 'allergenTags', value: string[]) => change(key, value);
  const writeable = canManage && !busy;
  return <>
    <CatalogSheet isOpen={isOpen} onClose={requestClose} title={item ? 'Ürünü düzenle' : 'Ürün ekle'}>
      <form className="catalog-list" onSubmit={submit}>
        <label className="catalog-field">Ürün adı<input autoFocus value={draft.name} onChange={(event) => change('name', event.target.value)} required maxLength={120} disabled={!writeable} /></label>
        <label className="catalog-field">Slug<input value={draft.slug} onChange={(event) => change('slug', event.target.value)} pattern="[a-z0-9]+(-[a-z0-9]+)*" required disabled={!writeable} /></label>
        <label className="catalog-field">Kategori<select value={draft.categoryId} onChange={(event) => change('categoryId', event.target.value)} required disabled={!writeable}>
          <option value="">Kategori seçin</option>{categories.filter((entry) => entry.isActive).map((entry) => <option key={entry.id} value={entry.id}>{entry.name}</option>)}
        </select></label>
        <label className="catalog-field">Temel fiyat (₺)<input type="number" min="0" step="0.01" value={(draft.basePriceMinorUnits / 100).toFixed(2)} disabled={!canPrice || !writeable}
          onChange={(event) => change('basePriceMinorUnits', Math.round(Number(event.target.value) * 100))} aria-describedby={!canPrice ? 'item-price-lock' : undefined} /></label>
        {!canPrice && <small id="item-price-lock">Fiyat alanı salt okunur; fiyat yönetimi izni gerekir.</small>}
        <label className="catalog-field">Kısa açıklama<input value={draft.shortDescription} onChange={(event) => change('shortDescription', event.target.value)} maxLength={240} disabled={!writeable} /></label>
        <label className="catalog-field">Açıklama<textarea value={draft.fullDescription} onChange={(event) => change('fullDescription', event.target.value)} maxLength={2000} disabled={!writeable} /></label>
        <label className="catalog-field">Görsel URL<input type="text" value={draft.imageUrl} placeholder="/images/urun.jpg veya https://…" onChange={(event) => change('imageUrl', event.target.value)} disabled={!writeable} />
          <small>Site içi güvenli yol veya HTTPS adresi kullanın.</small></label>
        <label className="catalog-field">Acılık seviyesi<select value={draft.spicyLevel} onChange={(event) => change('spicyLevel', Number(event.target.value))} disabled={!writeable}>
          <option value={0}>Acısız</option><option value={1}>Hafif</option><option value={2}>Orta</option><option value={3}>Acı</option>
        </select></label>
        <DietaryAndAllergenFields dietaryTags={draft.dietaryTags} allergenTags={draft.allergenTags}
          onDietaryChange={(tags) => setTags('dietaryTags', tags)} onAllergenChange={(tags) => setTags('allergenTags', tags)} />
        {error && <p role="alert" className="catalog-message catalog-error">{error}</p>}
        <div className="catalog-actions"><Button type="submit" loading={busy} disabled={!writeable}>Kaydet</Button><Button type="button" variant="outline" onClick={requestClose}>Kapat</Button></div>
      </form>
      {item && !dirty && <div className="catalog-list">
        <VariantEditor branchId={branchId} menuId={menuId} item={item} canManage={canManage} canPrice={canPrice} onSaved={onNestedSaved} onError={onError} />
        <ModifierGroupEditor branchId={branchId} menuId={menuId} item={item} groups={groups} canManage={canManage} canPrice={canPrice} onSaved={onNestedSaved} onError={onError} />
      </div>}
    </CatalogSheet>
    <ConfirmationDialog isOpen={confirmClose} title="Değişiklikleri sil?" message="Kaydedilmemiş ürün değişiklikleri kaybolacak."
      confirmLabel="Değişiklikleri sil" onCancel={() => setConfirmClose(false)} onConfirm={() => { setConfirmClose(false); onClose(); }} />
  </>;
};
