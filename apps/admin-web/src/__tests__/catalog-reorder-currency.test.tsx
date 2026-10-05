import { describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { CategoryContract, MenuItemContract, VariantContract, ModifierGroupContract } from '@restaurant-order/contracts';
import { CategoryList } from '../catalog/CategoryList';
import { MenuItemList } from '../catalog/MenuItemList';
import { VariantEditor } from '../catalog/VariantEditor';
import { ModifierGroupEditor } from '../catalog/ModifierGroupEditor';
import { CatalogPreviewSheet } from '../catalog/CatalogPreviewSheet';
import { formatCurrency, toMinorUnits, toMajorUnits } from '../catalog/currencyUtils';
import { catalogApi } from '../catalog/catalogApi';

describe('catalog reordering and currency formatting UI', () => {
  const cat1: CategoryContract = { id: 'c1', tenantId: 't1', branchId: 'b1', menuId: 'm1', name: 'Çorbalar', slug: 'corbalar', description: '', sortOrder: 0, isActive: true, concurrencyToken: 'ct1' };
  const cat2: CategoryContract = { id: 'c2', tenantId: 't1', branchId: 'b1', menuId: 'm1', name: 'Ana Yemekler', slug: 'ana-yemekler', description: '', sortOrder: 1, isActive: true, concurrencyToken: 'ct2' };

  const item1: MenuItemContract = {
    id: 'i1', tenantId: 't1', branchId: 'b1', menuId: 'm1', categoryId: 'c1', name: 'Mercimek', slug: 'mercimek', shortDescription: '', fullDescription: '',
    imageUrl: null, basePriceMinorUnits: 7500, sortOrder: 0, isActive: true, spicyLevel: 0,
    dietaryTags: [], allergenTags: [], variants: [], modifierGroups: [], concurrencyToken: 'it1',
  };
  const item2: MenuItemContract = {
    id: 'i2', tenantId: 't1', branchId: 'b1', menuId: 'm1', categoryId: 'c1', name: 'Ezogelin', slug: 'ezogelin', shortDescription: '', fullDescription: '',
    imageUrl: null, basePriceMinorUnits: 8000, sortOrder: 1, isActive: true, spicyLevel: 0,
    dietaryTags: [], allergenTags: [], variants: [], modifierGroups: [], concurrencyToken: 'it2',
  };

  const var1: VariantContract = { id: 'v1', name: 'Küçük', code: 'SML', absolutePriceMinorUnits: 6000, sortOrder: 0, isDefault: true, isActive: true, concurrencyToken: 'vt1' };
  const var2: VariantContract = { id: 'v2', name: 'Büyük', code: 'BIG', absolutePriceMinorUnits: 9000, sortOrder: 1, isDefault: false, isActive: true, concurrencyToken: 'vt2' };

  it('formats currency correctly for TRY, EUR, GBP and fallback handling', () => {
    const tryFormatted = formatCurrency(12550, 'TRY');
    const eurFormatted = formatCurrency(12550, 'EUR');
    const gbpFormatted = formatCurrency(12550, 'GBP');
    const defaultFormatted = formatCurrency(12550);
    const fallbackFormatted = formatCurrency(12550, 'UNKNOWN_INVALID_CURRENCY');

    expect(tryFormatted).toContain('125,50');
    expect(eurFormatted).toContain('125,50');
    expect(gbpFormatted).toContain('125,50');
    expect(defaultFormatted).toContain('125,50');
    expect(fallbackFormatted).toContain('125.50');
  });

  it('converts correctly between major and minor units with edge cases', () => {
    expect(toMinorUnits(12.55)).toBe(1255);
    expect(toMinorUnits('12.55')).toBe(1255);
    expect(toMinorUnits('invalid')).toBe(0);
    expect(toMajorUnits(1255)).toBe('12.55');
    expect(toMajorUnits(0)).toBe('0.00');
  });

  it('supports accessible up/down reordering for categories', async () => {
    const onReorder = vi.fn().mockResolvedValue(undefined);
    render(<CategoryList categories={[cat1, cat2]} selectedCategoryId="c1" canManage onSelect={vi.fn()} onEdit={vi.fn()} onCreate={vi.fn()} onToggle={vi.fn()} onReorder={onReorder} />);

    // cat1 is at index 0, so up button should be disabled, down button enabled
    const downButtons = screen.getAllByRole('button', { name: /aşağı taşı/i });
    expect(downButtons).toHaveLength(2);
    fireEvent.click(downButtons[0]!);

    await waitFor(() => {
      expect(onReorder).toHaveBeenCalledWith([cat2, cat1]);
    });
  });

  it('supports accessible up/down reordering for items', async () => {
    const onReorder = vi.fn().mockResolvedValue(undefined);
    render(<MenuItemList items={[item1, item2]} availability={[]} currency="EUR" canManage canPrice canQuick86 onEdit={vi.fn()} onCreate={vi.fn()} onToggleStatus={vi.fn()} onAvailabilityChanged={vi.fn()} onReorder={onReorder} onError={vi.fn()} />);

    // Check currency display in list
    expect(screen.getByText(/75,50|75,00/)).toBeTruthy();

    const downButtons = screen.getAllByRole('button', { name: /aşağı taşı/i });
    expect(downButtons.length).toBeGreaterThan(0);
    fireEvent.click(downButtons[0]!);

    await waitFor(() => {
      expect(onReorder).toHaveBeenCalledWith([item2, item1]);
    });
  });

  it('supports accessible up/down reordering for variants calling catalogApi', async () => {
    const reorderSpy = vi.spyOn(catalogApi, 'reorderVariants').mockResolvedValue([var2, var1]);
    const onSaved = vi.fn().mockResolvedValue(undefined);
    const itemWithVars = { ...item1, variants: [var1, var2] };

    render(<VariantEditor branchId="b1" menuId="m1" item={itemWithVars} currency="GBP" canManage canPrice onSaved={onSaved} onError={vi.fn()} />);

    const downButtons = screen.getAllByRole('button', { name: 'Aşağı taşı' });
    fireEvent.click(downButtons[0]!);

    await waitFor(() => {
      expect(reorderSpy).toHaveBeenCalledWith('b1', 'm1', 'i1', [
        { id: 'v2', sortOrder: 0, concurrencyToken: 'vt2' },
        { id: 'v1', sortOrder: 1, concurrencyToken: 'vt1' },
      ]);
      expect(onSaved).toHaveBeenCalled();
    });
    reorderSpy.mockRestore();
  });

  it('supports accessible up/down reordering for modifier options calling catalogApi', async () => {
    const group: ModifierGroupContract = {
      id: 'g1', branchId: 'b1', name: 'Soslar', minSelections: 0, maxSelections: 2, sortOrder: 0, isActive: true, concurrencyToken: 'gt1',
      options: [
        { id: 'o1', modifierGroupId: 'g1', name: 'Ketçap', priceDeltaMinorUnits: 500, sortOrder: 0, isDefault: false, isActive: true, concurrencyToken: 'ot1' },
        { id: 'o2', modifierGroupId: 'g1', name: 'Mayonez', priceDeltaMinorUnits: 600, sortOrder: 1, isDefault: false, isActive: true, concurrencyToken: 'ot2' },
      ],
    };
    const reorderSpy = vi.spyOn(catalogApi, 'reorderModifierOptions').mockResolvedValue(group.options!);
    const onSaved = vi.fn().mockResolvedValue(undefined);

    render(<ModifierGroupEditor branchId="b1" menuId="m1" item={item1} groups={[group]} currency="EUR" canManage canPrice onSaved={onSaved} onError={vi.fn()} />);

    const downButtons = screen.getAllByRole('button', { name: 'Aşağı taşı' });
    fireEvent.click(downButtons[0]!);

    await waitFor(() => {
      expect(reorderSpy).toHaveBeenCalledWith('b1', 'g1', [
        { id: 'o2', sortOrder: 0, concurrencyToken: 'ot2' },
        { id: 'o1', sortOrder: 1, concurrencyToken: 'ot1' },
      ]);
      expect(onSaved).toHaveBeenCalled();
    });
    reorderSpy.mockRestore();
  });

  it('displays branch currency in catalog preview sheet', () => {
    render(<CatalogPreviewSheet isOpen menu={{ id: 'm1', tenantId: 't1', branchId: 'b1', name: 'Menü', slug: 'menu', description: '', sortOrder: 0, status: 'Active', concurrencyToken: 'mt1' }}
      categories={[cat1]} items={[{ ...item1, variants: [var1] }]} currency="GBP" onClose={vi.fn()} />);

    // Prices are formatted with GBP
    expect(screen.getByText(/75,00/)).toBeTruthy();
    expect(screen.getByText(/60,00/)).toBeTruthy();
  });
});
