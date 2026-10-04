import React, { useState } from 'react';
import { Button, ConfirmationDialog } from '@restaurant-order/ui';
import { CatalogSheet } from './CatalogSheet';
import type { MenuContract } from '@restaurant-order/contracts';
import { MenuStatusBadge } from './MenuStatusBadge';

interface Props {
  menus: MenuContract[];
  selectedMenuId: string;
  canManage: boolean;
  onSelect: (id: string) => void;
  onCreate: (data: { name: string; slug: string; description: string; sortOrder: number }) => Promise<void>;
  onEdit: (menu: MenuContract, data: { name: string; description: string; sortOrder: number }) => Promise<void>;
  onStatusChange: (menu: MenuContract, action: 'activate' | 'archive') => void;
}

export const MenuSelector: React.FC<Props> = ({ menus, selectedMenuId, canManage, onSelect, onCreate, onEdit, onStatusChange }) => {
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<MenuContract | null>(null);
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [validation, setValidation] = useState('');
  const [confirmClose, setConfirmClose] = useState(false);
  const [saved, setSaved] = useState({ name: '', slug: '', description: '' });
  const start = (menu?: MenuContract) => {
    setEditing(menu ?? null);
    setName(menu?.name ?? '');
    setSlug(menu?.slug ?? '');
    setDescription(menu?.description ?? '');
    setSaved({ name: menu?.name ?? '', slug: menu?.slug ?? '', description: menu?.description ?? '' });
    setValidation('');
    setOpen(true);
  };
  const dirty = name !== saved.name || slug !== saved.slug || description !== saved.description;
  const requestClose = () => dirty ? setConfirmClose(true) : setOpen(false);
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim() || (!editing && !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug))) {
      setValidation('Menü adı zorunlu; slug küçük harf, sayı ve tire içermelidir.');
      return;
    }
    setBusy(true);
    try {
      const data = { name: name.trim(), slug: slug.trim(), description: description.trim(), sortOrder: editing?.sortOrder ?? menus.length };
      if (editing) await onEdit(editing, data);
      else await onCreate(data);
      setOpen(false);
    } catch (reason) {
      setValidation(reason instanceof Error ? reason.message : 'Menü kaydedilemedi.');
    } finally { setBusy(false); }
  };

  return (
    <section className="catalog-panel" aria-label="Menü seçimi">
      <div className="catalog-toolbar">
        <label className="catalog-field" htmlFor="catalog-menu-select"><span>Menü</span>
          <select id="catalog-menu-select" className="catalog-select" value={selectedMenuId} onChange={(event) => onSelect(event.target.value)}>
            <option value="">Menü seçin</option>
            {menus.map((menu) => <option key={menu.id} value={menu.id}>{menu.name} · {menu.status}</option>)}
          </select>
        </label>
        {canManage && <Button size="md" variant="outline" onClick={() => start()}>Menü ekle</Button>}
      </div>
      {menus.length === 0 && <p role="status">Bu şube için henüz menü yok.</p>}
      {menus.find((menu) => menu.id === selectedMenuId) && (() => {
        const selected = menus.find((menu) => menu.id === selectedMenuId)!;
        return <div className="catalog-row"><div className="catalog-actions"><MenuStatusBadge status={selected.status} /><span>{selected.description}</span></div>
          {canManage && <div className="catalog-actions">
            <Button size="md" variant="outline" onClick={() => start(selected)}>Menüyü düzenle</Button>
            {selected.status === 'Draft' && <Button size="md" variant="outline" onClick={() => onStatusChange(selected, 'activate')}>Yayınla</Button>}
            {selected.status !== 'Archived' && <Button size="md" variant="danger" onClick={() => onStatusChange(selected, 'archive')}>Arşivle</Button>}
          </div>}
        </div>;
      })()}
      <CatalogSheet isOpen={open} onClose={requestClose} title={editing ? 'Menüyü düzenle' : 'Yeni menü'}>
        <form onSubmit={submit} className="catalog-list">
          <label className="catalog-field">Menü adı<input autoFocus value={name} onChange={(event) => setName(event.target.value)} required maxLength={120} /></label>
          {!editing && <label className="catalog-field">Slug<input value={slug} onChange={(event) => setSlug(event.target.value)} required pattern="[a-z0-9]+(-[a-z0-9]+)*" aria-describedby="menu-slug-help" /><span id="menu-slug-help">Küçük harf, sayı ve tire kullanın.</span></label>}
          <label className="catalog-field">Açıklama<textarea value={description} onChange={(event) => setDescription(event.target.value)} maxLength={500} /></label>
          {validation && <p role="alert" className="catalog-message catalog-error">{validation}</p>}
          <Button type="submit" loading={busy}>Kaydet</Button>
        </form>
      </CatalogSheet>
      <ConfirmationDialog isOpen={confirmClose} title="Değişiklikleri sil?" message="Kaydedilmemiş menü değişiklikleri kaybolacak."
        confirmLabel="Değişiklikleri sil" onCancel={() => setConfirmClose(false)} onConfirm={() => { setConfirmClose(false); setOpen(false); }} />
    </section>
  );
};
