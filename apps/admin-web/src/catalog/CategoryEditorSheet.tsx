import React, { useMemo, useState } from 'react';
import { Button, ConfirmationDialog } from '@restaurant-order/ui';
import { CatalogSheet } from './CatalogSheet';
import type { CategoryContract } from '@restaurant-order/contracts';

interface Props {
  isOpen: boolean;
  category: CategoryContract | null;
  categories: CategoryContract[];
  onClose: () => void;
  onSave: (data: { name: string; slug: string; description: string; sortOrder: number }, category?: CategoryContract) => Promise<void>;
}

export const CategoryEditorSheet: React.FC<Props> = ({ isOpen, category, categories, onClose, onSave }) => {
  const initial = useMemo(() => ({ name: category?.name ?? '', slug: category?.slug ?? '', description: category?.description ?? '' }), [category]);
  const [name, setName] = useState(initial.name);
  const [slug, setSlug] = useState(initial.slug);
  const [description, setDescription] = useState(initial.description);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirmClose, setConfirmClose] = useState(false);
  React.useEffect(() => { setName(initial.name); setSlug(initial.slug); setDescription(initial.description); setError(''); }, [initial, isOpen]);
  const dirty = name !== initial.name || slug !== initial.slug || description !== initial.description;
  const close = () => dirty ? setConfirmClose(true) : onClose();
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim() || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug)) { setError('Ad zorunludur; slug küçük harf, sayı ve tire içermelidir.'); return; }
    if (categories.some((entry) => entry.id !== category?.id && entry.slug === slug)) { setError('Bu slug bu menüde zaten kullanılıyor.'); return; }
    setBusy(true); setError('');
    try { await onSave({ name: name.trim(), slug, description: description.trim(), sortOrder: category?.sortOrder ?? categories.length }, category ?? undefined); onClose(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Kategori kaydedilemedi.'); }
    finally { setBusy(false); }
  };
  return <>
    <CatalogSheet isOpen={isOpen} onClose={close} title={category ? 'Kategoriyi düzenle' : 'Kategori ekle'}>
      <form className="catalog-list" onSubmit={submit}>
        <label className="catalog-field">Kategori adı<input autoFocus value={name} onChange={(event) => setName(event.target.value)} maxLength={100} required /></label>
        <label className="catalog-field">Slug<input value={slug} onChange={(event) => setSlug(event.target.value)} maxLength={120} pattern="[a-z0-9]+(-[a-z0-9]+)*" required aria-describedby="category-slug-help" /></label>
        <small id="category-slug-help">Küçük harf, sayı ve tire kullanın.</small>
        <label className="catalog-field">Açıklama<textarea value={description} onChange={(event) => setDescription(event.target.value)} maxLength={500} /></label>
        {error && <p role="alert" className="catalog-message catalog-error">{error}</p>}
        <Button type="submit" loading={busy}>Kaydet</Button>
      </form>
    </CatalogSheet>
    <ConfirmationDialog isOpen={confirmClose} title="Değişiklikleri sil?" message="Kaydedilmemiş kategori değişiklikleri kaybolacak."
      confirmLabel="Değişiklikleri sil" onCancel={() => setConfirmClose(false)} onConfirm={() => { setConfirmClose(false); onClose(); }} />
  </>;
};
