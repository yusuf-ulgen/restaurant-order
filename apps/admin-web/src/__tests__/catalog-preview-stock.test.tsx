import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { UserPrincipalDto } from '@restaurant-order/contracts';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider, createDefaultFallbackTheme } from '../config/AdminConfigContext';
import { MenuCatalogView } from '../catalog/MenuCatalogView';

const branch = 'branch-view';
const menu = { id: 'menu-view', tenantId: 'tenant-view', branchId: branch, name: 'Dinner', slug: 'dinner', description: 'Today', status: 'Active', sortOrder: 0, concurrencyToken: 'menu-token' };
const category = { id: 'category-view', tenantId: 'tenant-view', branchId: branch, menuId: menu.id, name: 'Soup', slug: 'soup', description: '', sortOrder: 0, isActive: true, concurrencyToken: 'category-token' };
const item = { id: 'item-view', tenantId: 'tenant-view', branchId: branch, menuId: menu.id, categoryId: category.id, name: 'Lentil', slug: 'lentil', shortDescription: 'Warm', fullDescription: '', imageUrl: null, basePriceMinorUnits: 1000, sortOrder: 0, isActive: true, spicyLevel: 0, dietaryTags: [], allergenTags: [], concurrencyToken: 'item-token', variants: [], modifierGroups: [] };
const user: UserPrincipalDto = { userId: 'user-view', email: 'user@example.test', role: 'RestaurantAdmin', tenantId: 'tenant-view', securityVersion: 1 };
const json = (data: unknown) => new Response(JSON.stringify(data), { status: 200, headers: { 'Content-Type': 'application/json' } });

describe('catalog preview and stock state', () => {
  it('previews the selected menu and restocks an unavailable item', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    let unavailable = true;
    const availability = () => unavailable ? [{ tenantId: 'tenant-view', branchId: branch, menuItemId: item.id, itemVariantId: null, isAvailable: false, reasonCode: 'Manual', note: '', concurrencyToken: 'availability-token' }] : [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      calls.push({ url, init });
      if (url.endsWith('/menus')) return json([menu]);
      if (url.endsWith('/availability')) return json(availability());
      if (url.endsWith('/categories')) return json([category]);
      if (url.endsWith('/items') && init?.method !== 'POST') return json([item]);
      if (url.endsWith('/modifier-groups')) return json([]);
      if (url.endsWith('/restock')) { unavailable = false; return json(availability()[0] ?? {}); }
      return json(item);
    }));
    render(<AuthProvider initialUser={user}><AdminConfigProvider initialTheme={createDefaultFallbackTheme(user.tenantId, branch)}
      initialBranches={[{ id: branch, brandId: 'brand-view', name: 'Main', slug: 'main', isActive: true, status: 'Active' }]} initialBranchId={branch}>
      <MenuCatalogView />
    </AdminConfigProvider></AuthProvider>);

    expect(await screen.findByText('Lentil')).toBeTruthy();
    fireEvent.click(screen.getAllByRole('button')[4]!);
    expect(screen.getByRole('dialog')).toBeTruthy();
    expect(screen.getByText('Today')).toBeTruthy();
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });

    await waitFor(() => expect(screen.getByRole('button', { name: 'Stok yenilendi' })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: 'Stok yenilendi' }));
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/restock') && call.init?.method === 'POST')).toBe(true));
    expect(new Headers(calls.find((call) => call.url.endsWith('/restock'))?.init?.headers).get('If-Match')).toBe('"availability-token"');
  });
});
