import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { MenuContract } from '@restaurant-order/contracts';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider, createDefaultFallbackTheme } from '../config/AdminConfigContext';
import { MenuSelector } from '../catalog/MenuSelector';
import { ModifierGroupEditor } from '../catalog/ModifierGroupEditor';
import { CategoryList } from '../catalog/CategoryList';
import { VariantEditor } from '../catalog/VariantEditor';
import { MenuStatusBadge } from '../catalog/MenuStatusBadge';
import { MenuItemList } from '../catalog/MenuItemList';

const branchId = 'branch-test';
const menuId = 'menu-test';
const group = {
  id: 'group-test', tenantId: 'tenant-test', branchId, name: 'Extras', minSelections: 0, maxSelections: 2,
  sortOrder: 0, isActive: true, concurrencyToken: 'group-token', options: [{
    id: 'option-test', tenantId: 'tenant-test', branchId, modifierGroupId: 'group-test', name: 'Cheese',
    priceDeltaMinorUnits: 100, sortOrder: 0, isDefault: false, isActive: true, concurrencyToken: 'option-token',
  }],
};
const user = { userId: 'user-test', email: 'user@example.test', role: 'RestaurantAdmin' as const, tenantId: 'tenant-test', securityVersion: 1 };
const respond = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } });

describe('catalog editors', () => {
  it('validates and creates a menu through the selector', async () => {
    const create = vi.fn().mockResolvedValue(undefined);
    render(<AuthProvider initialUser={user}><AdminConfigProvider initialTheme={createDefaultFallbackTheme('tenant-test', branchId)}>
      <MenuSelector menus={[]} selectedMenuId="" canManage onSelect={vi.fn()} onCreate={create} onEdit={vi.fn()} onStatusChange={vi.fn()} />
    </AdminConfigProvider></AuthProvider>);

    fireEvent.click(screen.getAllByRole('button')[0]!);
    const textboxes = screen.getAllByRole('textbox');
    fireEvent.change(textboxes[0]!, { target: { value: 'Dinner' } });
    fireEvent.change(textboxes[1]!, { target: { value: 'dinner' } });
    fireEvent.submit(textboxes[0]!.closest('form')!);
    await waitFor(() => expect(create).toHaveBeenCalledWith({ name: 'Dinner', slug: 'dinner', description: '', sortOrder: 0 }));
  });

  it('edits a selected menu and exposes its publish action', async () => {
    const selectedMenu = { id: menuId, branchId, tenantId: 'tenant-test', name: 'Dinner', slug: 'dinner', description: '', status: 'Draft', sortOrder: 0, concurrencyToken: 'menu-token' } as MenuContract;
    const edit = vi.fn().mockResolvedValue(undefined);
    const statusChange = vi.fn();
    render(<AuthProvider initialUser={user}><AdminConfigProvider initialTheme={createDefaultFallbackTheme('tenant-test', branchId)}>
      <MenuSelector menus={[selectedMenu]} selectedMenuId={menuId} canManage onSelect={vi.fn()} onCreate={vi.fn()}
        onEdit={edit} onStatusChange={statusChange} />
    </AdminConfigProvider></AuthProvider>);
    fireEvent.click(screen.getAllByRole('button')[1]!);
    fireEvent.change(screen.getAllByRole('textbox')[0]!, { target: { value: 'Dinner menu' } });
    fireEvent.submit(screen.getAllByRole('textbox')[0]!.closest('form')!);
    await waitFor(() => expect(edit).toHaveBeenCalledWith(selectedMenu, { name: 'Dinner menu', slug: 'dinner', description: '', sortOrder: 0 }));
    fireEvent.click(screen.getAllByRole('button')[2]!);
    expect(statusChange).toHaveBeenCalledWith(selectedMenu, 'activate');
    fireEvent.click(screen.getAllByRole('button')[1]!);
    fireEvent.change(screen.getAllByRole('textbox')[1]!, { target: { value: 'Unsaved description' } });
    fireEvent.click(screen.getByRole('button', { name: 'Kapat' }));
    expect(screen.getByText('Değişiklikleri sil?')).toBeTruthy();
    fireEvent.click(screen.getAllByRole('button').at(-1)!);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('confirms a category visibility change before calling the mutation', async () => {
    const toggle = vi.fn().mockResolvedValue(undefined);
    const category = { id: 'category-test', name: 'Soup', isActive: true } as never;
    render(<CategoryList categories={[category]} selectedCategoryId="category-test" canManage onSelect={vi.fn()}
      onEdit={vi.fn()} onCreate={vi.fn()} onToggle={toggle} />);
    fireEvent.click(screen.getAllByRole('button')[3]!);
    expect(toggle).not.toHaveBeenCalled();
    fireEvent.click(screen.getAllByRole('button').at(-1)!);
    await waitFor(() => expect(toggle).toHaveBeenCalledWith(category));
  });

  it('keeps category confirmation open when the visibility request fails', async () => {
    const toggle = vi.fn().mockRejectedValue(new Error('network failure'));
    const category = { id: 'category-failure', name: 'Soup', isActive: true } as never;
    render(<CategoryList categories={[category]} selectedCategoryId="category-failure" canManage onSelect={vi.fn()}
      onEdit={vi.fn()} onCreate={vi.fn()} onToggle={toggle} />);
    fireEvent.click(screen.getAllByRole('button')[3]!);
    fireEvent.click(screen.getAllByRole('button').at(-1)!);
    await waitFor(() => expect(toggle).toHaveBeenCalledWith(category));
    expect(screen.getByRole('dialog')).toBeTruthy();
  });

  it('allows editing variant details without sending a protected price', async () => {
    const variant = { id: 'variant-test', name: 'Regular', code: 'REG', absolutePriceMinorUnits: 1000, sortOrder: 0, isDefault: true, concurrencyToken: 'variant-token' };
    const item = { id: 'item-test', variants: [variant] } as never;
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => { requests.push({ url, init }); return Promise.resolve(new Response(JSON.stringify(variant))); }));
    render(<VariantEditor branchId={branchId} menuId={menuId} item={item} canManage canPrice={false} onSaved={vi.fn().mockResolvedValue(undefined)} onError={vi.fn()} />);
    fireEvent.click(screen.getAllByRole('button')[0]!);
    fireEvent.change(screen.getAllByRole('textbox')[0]!, { target: { value: 'Regular size' } });
    fireEvent.submit(screen.getAllByRole('textbox')[0]!.closest('form')!);
    await waitFor(() => expect(requests.some((request) => request.url.endsWith('/variants/variant-test') && request.init?.method === 'PUT')).toBe(true));
    const body = JSON.parse(String(requests[0]!.init?.body));
    expect(body).toMatchObject({ name: 'Regular size', code: 'REG', concurrencyToken: 'variant-token' });
    expect(body).not.toHaveProperty('absolutePriceMinorUnits');
  });

  it('renders all menu status variants and the empty read-only category state', () => {
    const view = render(<><MenuStatusBadge status="Draft" /><MenuStatusBadge status="Archived" /><MenuStatusBadge status="Custom" />
      <CategoryList categories={[]} selectedCategoryId="" canManage={false} onSelect={vi.fn()} onEdit={vi.fn()} onCreate={vi.fn()} onToggle={vi.fn()} /></>);
    expect(screen.getByText('Custom')).toBeTruthy();
    expect(screen.getByRole('status')).toBeTruthy();
    expect(screen.queryByRole('button')).toBeNull();
    view.unmount();
  });

  it('hides protected item creation from a manager without pricing permission and confirms item visibility changes', async () => {
    const item = { id: 'item-list', name: 'Soup', shortDescription: '', isActive: true, basePriceMinorUnits: 1000, variants: [] } as never;
    const toggle = vi.fn().mockResolvedValue(undefined);
    render(<MenuItemList items={[item]} availability={[]} canManage canPrice={false} canQuick86={false}
      onEdit={vi.fn()} onCreate={vi.fn()} onToggleStatus={toggle} onAvailabilityChanged={vi.fn()} onError={vi.fn()} />);
    expect((screen.getAllByRole('button')[0]! as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getAllByRole('button')[2]!);
    fireEvent.click(screen.getAllByRole('button').at(-1)!);
    await waitFor(() => expect(toggle).toHaveBeenCalledWith(item));
    expect(screen.getByLabelText('Stok durumu').textContent).toBe('Stokta');
  });

  it('updates modifier bounds and options without submitting a protected price', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      requests.push({ url, init });
      if (url.endsWith('/modifier-groups/group-test')) return respond(group);
      if (url.endsWith('/modifier-groups/group-test/options/option-test')) return respond(group.options[0]);
      return respond({});
    }));
    const saved = vi.fn().mockResolvedValue(undefined);
    render(<ModifierGroupEditor branchId={branchId} menuId={menuId} item={null} groups={[group]}
      canManage canPrice={false} onSaved={saved} onError={vi.fn()} />);

    fireEvent.click(screen.getAllByRole('button')[1]!);
    const bounds = screen.getAllByRole('spinbutton');
    fireEvent.change(bounds[0]!, { target: { value: '1' } });
    fireEvent.change(bounds[1]!, { target: { value: '3' } });
    fireEvent.submit(bounds[0]!.closest('form')!);
    await waitFor(() => expect(requests.some((request) => request.url.endsWith('/modifier-groups/group-test') && request.init?.method === 'PUT')).toBe(true));

    fireEvent.click(screen.getAllByRole('button')[3]!);
    expect((screen.getAllByRole('spinbutton')[2]! as HTMLInputElement).disabled).toBe(true);
    const optionName = screen.getAllByRole('textbox')[1]!;
    fireEvent.change(optionName, { target: { value: 'Aged cheese' } });
    fireEvent.submit(optionName.closest('form')!);
    await waitFor(() => expect(requests.some((request) => request.url.endsWith('/options/option-test') && request.init?.method === 'PUT')).toBe(true));
    const update = requests.find((request) => request.url.endsWith('/options/option-test'));
    expect(JSON.parse(String(update?.init?.body))).toMatchObject({ name: 'Aged cheese', concurrencyToken: 'option-token' });
    expect(JSON.parse(String(update?.init?.body))).not.toHaveProperty('priceDeltaMinorUnits');
  });
});
