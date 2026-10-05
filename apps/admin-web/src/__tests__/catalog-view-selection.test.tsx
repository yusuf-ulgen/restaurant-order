import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { UserPrincipalDto } from '@restaurant-order/contracts';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider, createDefaultFallbackTheme } from '../config/AdminConfigContext';
import { MenuCatalogView } from '../catalog/MenuCatalogView';

const branch = 'branch-select';
const firstMenu = { id: 'menu-a', tenantId: 'tenant-select', branchId: branch, name: 'Menu A', slug: 'menu-a', description: '', status: 'Active', sortOrder: 0, concurrencyToken: 'a-token' };
const secondMenu = { ...firstMenu, id: 'menu-b', name: 'Menu B', slug: 'menu-b', sortOrder: 1, concurrencyToken: 'b-token' };
const categories = [
  { id: 'category-a', tenantId: 'tenant-select', branchId: branch, menuId: secondMenu.id, name: 'Dessert', slug: 'dessert', description: '', sortOrder: 0, isActive: true, concurrencyToken: 'ca-token' },
  { id: 'category-b', tenantId: 'tenant-select', branchId: branch, menuId: secondMenu.id, name: 'Sides', slug: 'sides', description: '', sortOrder: 1, isActive: true, concurrencyToken: 'cb-token' },
];
const itemFor = (id: string, categoryId: string, name: string) => ({ id, tenantId: 'tenant-select', branchId: branch, menuId: secondMenu.id, categoryId, name, slug: name.toLowerCase(), shortDescription: '', fullDescription: '', imageUrl: null, basePriceMinorUnits: 1000, sortOrder: 0, isActive: true, spicyLevel: 0, dietaryTags: [], allergenTags: [], concurrencyToken: `${id}-token`, variants: [], modifierGroups: [] });
const items = [itemFor('item-a', categories[0]!.id, 'Cake'), itemFor('item-b', categories[1]!.id, 'Fries')];
const user: UserPrincipalDto = { userId: 'user-select', email: 'user@example.test', role: 'RestaurantAdmin', tenantId: 'tenant-select', securityVersion: 1 };
const json = (value: unknown) => new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } });

describe('catalog menu and category selection', () => {
  it('reloads menu data and filters products when the selected category changes', async () => {
    const calls: string[] = [];
    vi.stubGlobal('fetch', vi.fn((url: string) => {
      calls.push(url);
      if (url.endsWith('/menus')) return json([firstMenu, secondMenu]);
      if (url.endsWith('/availability')) return json([]);
      if (url.endsWith('/categories')) return json(url.includes('menu-b') ? categories : []);
      if (url.includes('/items')) return json(url.includes('menu-b') ? items : []);
      if (url.endsWith('/modifier-groups')) return json([]);
      return json({});
    }));
    render(<AuthProvider initialUser={user}><AdminConfigProvider initialTheme={createDefaultFallbackTheme(user.tenantId, branch)}
      initialBranches={[{ id: branch, brandId: 'brand-select', name: 'Main', slug: 'main', isActive: true, status: 'Active' }]} initialBranchId={branch}>
      <MenuCatalogView />
    </AdminConfigProvider></AuthProvider>);

    await waitFor(() => expect((screen.getAllByRole('combobox')[0]! as HTMLSelectElement).value).toBe(firstMenu.id));
    fireEvent.change(screen.getAllByRole('combobox')[0]!, { target: { value: secondMenu.id } });
    expect(await screen.findByText('Cake')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Sides' }));
    await waitFor(() => expect(screen.getByText('Fries')).toBeTruthy());
    expect(screen.queryByText('Cake')).toBeNull();
    expect(calls.some((url) => url.includes('/menus/menu-b/items?categoryId='))).toBe(false);
  });
});
