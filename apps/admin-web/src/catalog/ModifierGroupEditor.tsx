import React, { useState } from 'react';
import { Button } from '@restaurant-order/ui';
import type { MenuItemContract, ModifierGroupContract, ModifierOptionContract } from '@restaurant-order/contracts';
import { catalogApi } from './catalogApi';
import { formatCurrency } from './currencyUtils';

interface Props {
  branchId: string;
  menuId: string;
  item: MenuItemContract | null;
  groups: ModifierGroupContract[];
  currency?: string;
  canManage: boolean;
  canPrice: boolean;
  onSaved: () => Promise<void>;
  onError: (error: unknown) => void;
}

export const ModifierGroupEditor: React.FC<Props> = ({
  branchId,
  menuId,
  item,
  groups,
  currency = 'TRY',
  canManage,
  canPrice,
  onSaved,
  onError,
}) => {
  const [group, setGroup] = useState<ModifierGroupContract | null>(null);
  const [groupName, setGroupName] = useState('');
  const [min, setMin] = useState(0);
  const [max, setMax] = useState(1);
  const [optionName, setOptionName] = useState('');
  const [optionPrice, setOptionPrice] = useState('0.00');
  const [option, setOption] = useState<ModifierOptionContract | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [reorderingOption, setReorderingOption] = useState(false);
  const assigned = new Set(item?.modifierGroups?.map((entry) => entry.modifierGroupId) ?? []);

  const mutate = async <T,>(work: () => Promise<T>): Promise<T> => {
    setBusy(true); setError('');
    try { const result = await work(); await onSaved(); return result; }
    catch (reason) { onError(reason); setError(reason instanceof Error ? reason.message : 'Değişiklik kaydedilemedi.'); throw reason; }
    finally { setBusy(false); }
  };

  const moveOption = async (targetGroup: ModifierGroupContract, index: number, offset: number) => {
    const options = targetGroup.options ?? [];
    if (reorderingOption || busy) return;
    const targetIndex = index + offset;
    if (targetIndex < 0 || targetIndex >= options.length) return;
    const reordered = [...options];
    const [moved] = reordered.splice(index, 1);
    if (!moved) return;
    reordered.splice(targetIndex, 0, moved);
    setReorderingOption(true);
    setError('');
    try {
      const items = reordered.map((o, idx) => ({
        id: o.id,
        sortOrder: idx,
        concurrencyToken: o.concurrencyToken,
      }));
      await catalogApi.reorderModifierOptions(branchId, targetGroup.id, items);
      await onSaved();
    } catch (reason) {
      onError(reason);
      setError(reason instanceof Error ? reason.message : 'Seçenek sıralaması güncellenemedi.');
    } finally {
      setReorderingOption(false);
    }
  };

  const saveGroup = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!groupName.trim() || min < 0 || max < min) { setError('Grup adı girin ve seçim sınırlarını 0 ≤ minimum ≤ maksimum olacak şekilde ayarlayın.'); return; }
    try {
      const saved = await mutate(() => catalogApi.saveModifierGroup(branchId, { name: groupName.trim(), minSelections: min, maxSelections: max, sortOrder: group?.sortOrder ?? groups.length }, group ?? undefined));
      setGroup(saved); setGroupName(saved.name); setMin(saved.minSelections); setMax(saved.maxSelections); setOption(null); setOptionName('');
    } catch { /* Parent renders the API error. */ }
  };

  const saveOption = (event: React.FormEvent) => {
    event.preventDefault();
    if (!group || !optionName.trim()) { setError('Seçenek adı zorunludur.'); return; }
    const priceDeltaMinorUnits = Math.round(Number(optionPrice) * 100);
    if (!Number.isSafeInteger(priceDeltaMinorUnits) || priceDeltaMinorUnits < 0) { setError('Fiyat farkı sıfır veya üzeri olmalıdır.'); return; }
    void mutate(() => catalogApi.saveModifierOption(branchId, group.id, {
      name: optionName.trim(), sortOrder: option?.sortOrder ?? (group.options?.length ?? 0), isDefault: option?.isDefault ?? false,
      ...(canPrice ? { priceDeltaMinorUnits } : {}),
    }, option ?? undefined));
  };

  const toggleAssignment = (target: ModifierGroupContract, isAssigned: boolean) => {
    if (!item) return;
    void mutate(() => isAssigned
      ? catalogApi.removeModifierGroup(branchId, menuId, item, target.id)
      : catalogApi.assignModifierGroup(branchId, menuId, item, target.id));
  };

  return <section className="catalog-panel" aria-labelledby="modifier-title">
    <h3 id="modifier-title">Seçenek grupları</h3>
    {!item && <p>Seçenek gruplarını ürünü kaydettikten sonra ürüne bağlayabilirsiniz.</p>}
    {item && <div className="catalog-list">
      {groups.map((entry) => <label className="catalog-check" key={entry.id}>
        <input type="checkbox" checked={assigned.has(entry.id)} disabled={!canManage || busy} onChange={(event) => toggleAssignment(entry, event.target.checked)} />
        {entry.name} ({entry.minSelections}–{entry.maxSelections})
      </label>)}
    </div>}
    {canManage && <form className="catalog-list" onSubmit={saveGroup}>
      <h4>{group ? 'Seçenek grubunu düzenle' : 'Yeni seçenek grubu'}</h4>
      <label className="catalog-field">Grup adı<input value={groupName} onChange={(event) => setGroupName(event.target.value)} maxLength={100} required /></label>
      <div className="catalog-toolbar">
        <label className="catalog-field">En az seçim<input type="number" min="0" value={min} onChange={(event) => setMin(Number(event.target.value))} /></label>
        <label className="catalog-field">En çok seçim<input type="number" min="0" value={max} onChange={(event) => setMax(Number(event.target.value))} /></label>
      </div>
      <div className="catalog-actions"><Button type="submit" loading={busy}>Grubu kaydet</Button>{group && <Button type="button" variant="outline" onClick={() => setGroup(null)}>Yeni grup</Button>}</div>
    </form>}
    {groups.map((entry) => <div className="catalog-panel" key={entry.id}>
      <div className="catalog-row"><strong>{entry.name}</strong>{canManage && <Button size="md" variant="outline" onClick={() => { setGroup(entry); setGroupName(entry.name); setMin(entry.minSelections); setMax(entry.maxSelections); }}>Düzenle</Button>}</div>
      {(entry.options ?? []).map((entryOption, idx) => <div className="catalog-row" key={entryOption.id}>
        <span className="catalog-wrap">{entryOption.name} · +{formatCurrency(entryOption.priceDeltaMinorUnits, currency)}</span>
        <div className="catalog-actions">
          {canManage && (entry.options?.length ?? 0) > 1 && (
            <div className="catalog-actions" role="group" aria-label={`${entryOption.name} sırasını değiştir`}>
              <Button size="sm" variant="outline" aria-label="Yukarı taşı" disabled={idx === 0 || reorderingOption || busy} onClick={() => moveOption(entry, idx, -1)}>↑</Button>
              <Button size="sm" variant="outline" aria-label="Aşağı taşı" disabled={idx === (entry.options?.length ?? 0) - 1 || reorderingOption || busy} onClick={() => moveOption(entry, idx, 1)}>↓</Button>
            </div>
          )}
          {canManage && <Button size="md" variant="outline" onClick={() => { setGroup(entry); setOption(entryOption); setOptionName(entryOption.name); setOptionPrice((entryOption.priceDeltaMinorUnits / 100).toFixed(2)); }}>Seçeneği düzenle</Button>}
        </div>
      </div>)}
      {canManage && group?.id === entry.id && <form className="catalog-list" onSubmit={saveOption}>
        <h4>{option ? 'Seçeneği düzenle' : 'Seçenek ekle'}</h4>
        <label className="catalog-field">Seçenek adı<input value={optionName} onChange={(event) => setOptionName(event.target.value)} required maxLength={100} /></label>
        <label className="catalog-field">{`Fiyat farkı (${currency})`}<input type="number" min="0" step="0.01" value={optionPrice} disabled={!canPrice} onChange={(event) => setOptionPrice(event.target.value)} /></label>
        {!canPrice && <small>Fiyat farkı düzenlemek için fiyat yönetimi izni gerekir.</small>}
        <div className="catalog-actions"><Button type="submit" loading={busy}>Seçeneği kaydet</Button><Button type="button" variant="outline" onClick={() => { setOption(null); setOptionName(''); setOptionPrice('0.00'); }}>Seçenek ekle</Button></div>
      </form>}
    </div>)}
    {error && <p role="alert" className="catalog-message catalog-error">{error}</p>}
  </section>;
};
