import React, { useState } from 'react';
import { Button } from '@restaurant-order/ui';
import type { MenuItemContract, VariantContract } from '@restaurant-order/contracts';
import { catalogApi } from './catalogApi';

interface Props { branchId: string; menuId: string; item: MenuItemContract; canManage: boolean; canPrice: boolean; onSaved: () => Promise<void>; onError: (error: unknown) => void }

const major = (minor: number) => (minor / 100).toFixed(2);
const minor = (value: string) => Math.round(Number(value) * 100);

export const VariantEditor: React.FC<Props> = ({ branchId, menuId, item, canManage, canPrice, onSaved, onError }) => {
  const variants = item.variants ?? [];
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [price, setPrice] = useState('0.00');
  const [isDefault, setIsDefault] = useState(false);
  const [editing, setEditing] = useState<VariantContract | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const begin = (variant?: VariantContract) => {
    setEditing(variant ?? null); setName(variant?.name ?? ''); setCode(variant?.code ?? '');
    setPrice(major(variant?.absolutePriceMinorUnits ?? 0)); setIsDefault(variant?.isDefault ?? false); setError('');
  };
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const amount = minor(price);
    if (!name.trim() || !/^[A-Za-z0-9_-]+$/.test(code) || !Number.isSafeInteger(amount) || amount < 0) { setError('Ad, geçerli kod ve sıfır veya üzeri bir fiyat girin.'); return; }
    setBusy(true); setError('');
    try {
      const data = { name: name.trim(), code: code.trim().toUpperCase(), sortOrder: editing?.sortOrder ?? variants.length, isDefault, ...(canPrice ? { absolutePriceMinorUnits: amount } : {}) };
      await catalogApi.saveVariant(branchId, menuId, item.id, data, editing ?? undefined);
      begin(); await onSaved();
    } catch (reason) { onError(reason); setError(reason instanceof Error ? reason.message : 'Varyant kaydedilemedi.'); }
    finally { setBusy(false); }
  };
  return <section className="catalog-panel" aria-labelledby="variant-title">
    <div className="catalog-section-heading"><h3 id="variant-title">Porsiyon ve varyantlar</h3></div>
    <div className="catalog-list">
      {variants.map((variant) => <div className="catalog-row" key={variant.id}>
        <span className="catalog-wrap">{variant.name} · {variant.code} · {(variant.absolutePriceMinorUnits / 100).toLocaleString('tr-TR', { style: 'currency', currency: 'TRY' })}{variant.isDefault ? ' · Varsayılan' : ''}</span>
        {canManage && <Button size="md" variant="outline" onClick={() => begin(variant)}>Düzenle</Button>}
      </div>)}
      {variants.length === 0 && <p role="status">Henüz varyant yok; ürün temel fiyatıyla satılır.</p>}
    </div>
    {canManage && <form className="catalog-list" onSubmit={submit}>
      <h4>{editing ? 'Varyantı düzenle' : 'Varyant ekle'}</h4>
      <label className="catalog-field">Varyant adı<input value={name} onChange={(event) => setName(event.target.value)} required maxLength={80} /></label>
      <label className="catalog-field">Kod<input value={code} onChange={(event) => setCode(event.target.value)} required pattern="[A-Za-z0-9_-]+" maxLength={32} /></label>
      <label className="catalog-field">Fiyat (₺)<input type="number" min="0" step="0.01" value={price} disabled={!canPrice} onChange={(event) => setPrice(event.target.value)} aria-describedby={!canPrice ? 'variant-price-lock' : undefined} /></label>
      {!canPrice && <small id="variant-price-lock">Fiyatı değiştirmek için fiyat yönetimi izni gerekir.</small>}
      <label className="catalog-check"><input type="checkbox" checked={isDefault} onChange={(event) => setIsDefault(event.target.checked)} />Varsayılan varyant</label>
      {!canPrice && !editing && <small>Yeni varyantın ilk fiyatını belirlemek için fiyat yönetimi izni gerekir.</small>}
      {error && <p role="alert" className="catalog-message catalog-error">{error}</p>}
      <div className="catalog-actions"><Button type="submit" loading={busy} disabled={!canPrice && !editing}>Varyantı kaydet</Button>{editing && <Button type="button" variant="outline" onClick={() => begin()}>Vazgeç</Button>}</div>
    </form>}
  </section>;
};
