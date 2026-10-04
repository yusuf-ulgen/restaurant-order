import { describe, expect, it, vi } from 'vitest';
import type { CategoryContract, VariantContract, MenuContract, MenuItemContract, ModifierGroupContract, ModifierOptionContract } from '@restaurant-order/contracts';
import { catalogApi } from '../catalog/catalogApi';

const branch = 'branch-api';
const menu = { id: 'menu-api', concurrencyToken: 'menu-token' } as unknown as MenuContract;
const category = { id: 'category-api', concurrencyToken: 'category-token' } as unknown as CategoryContract;
const item = { id: 'item-api', concurrencyToken: 'item-token' } as unknown as MenuItemContract;
const variant = { id: 'variant-api', concurrencyToken: 'variant-token' } as unknown as VariantContract;
const group = { id: 'group-api', concurrencyToken: 'group-token' } as unknown as ModifierGroupContract;
const option = { id: 'option-api', concurrencyToken: 'option-token' } as unknown as ModifierOptionContract;
const ok = (status = 200) => new Response(status === 204 ? null : '{}', { status });

describe('catalog API request contracts', () => {
  it('builds CRUD, state, assignment, and availability endpoint requests', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      calls.push({ url, init });
      return Promise.resolve(ok());
    }));

    await catalogApi.listMenus(branch);
    await catalogApi.createMenu(branch, { name: 'Dinner', slug: 'dinner', description: '', sortOrder: 0 });
    await catalogApi.updateMenu(branch, menu, { name: 'Dinner', description: '', sortOrder: 0 });
    await catalogApi.setMenuState(branch, menu, 'archive');
    await catalogApi.listCategories(branch, menu.id);
    await catalogApi.saveCategory(branch, menu.id, { name: 'Food', slug: 'food', description: '', sortOrder: 0 }, category);
    await catalogApi.setCategoryState(branch, menu.id, category, false);
    await catalogApi.setCategoryState(branch, menu.id, category, true);
    await catalogApi.listItems(branch, menu.id);
    await catalogApi.listItems(branch, menu.id, category.id);
    await catalogApi.saveItem(branch, menu.id, { name: 'Soup' });
    await catalogApi.saveItem(branch, menu.id, { name: 'Soup' }, item);
    await catalogApi.updateItemPrice(branch, menu.id, item, 1250);
    await catalogApi.setItemState(branch, menu.id, item, false);
    await catalogApi.updateMetadata(branch, menu.id, item, { spicyLevel: 0, dietaryTags: [], allergenTags: [] });
    await catalogApi.listVariants(branch, menu.id, item.id);
    await catalogApi.saveVariant(branch, menu.id, item.id, { name: 'Large' });
    await catalogApi.saveVariant(branch, menu.id, item.id, { name: 'Large' }, variant);
    await catalogApi.updateVariantPrice(branch, menu.id, item.id, variant, 1500);
    await catalogApi.listModifierGroups(branch);
    await catalogApi.saveModifierGroup(branch, { name: 'Extras' });
    await catalogApi.saveModifierGroup(branch, { name: 'Extras' }, group);
    await catalogApi.saveModifierOption(branch, group.id, { name: 'Cheese' });
    await catalogApi.saveModifierOption(branch, group.id, { name: 'Cheese' }, option);
    await catalogApi.updateModifierOptionPrice(branch, group.id, option, 250);
    await catalogApi.assignModifierGroup(branch, menu.id, item, group.id);
    await catalogApi.removeModifierGroup(branch, menu.id, item, group.id);
    await catalogApi.listAvailability(branch);
    await catalogApi.setAvailability(branch, menu.id, item, undefined, false);
    await catalogApi.setAvailability(branch, menu.id, item, undefined, true);

    expect(calls.find((call) => call.url.endsWith('/items?categoryId=category-api'))?.init?.method).toBe('GET');
    const itemUpdate = calls.find((call) => call.url.endsWith('/items/item-api'))!;
    expect(itemUpdate.init?.method).toBe('PUT');
    expect(new Headers(itemUpdate.init?.headers).get('If-Match')).toBe('"item-token"');
    expect(calls.some((call) => call.url.endsWith('/quick-86') && call.init?.method === 'POST')).toBe(true);
    expect(calls.some((call) => call.url.endsWith('/restock') && call.init?.method === 'POST')).toBe(true);
  });

  it('handles empty success responses and hides server details in API errors', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(ok(204)).mockResolvedValueOnce(new Response('{', { status: 409 }));
    vi.stubGlobal('fetch', fetchMock);
    await expect(catalogApi.removeModifierGroup(branch, menu.id, item, group.id)).resolves.toBeUndefined();
    await expect(catalogApi.listMenus(branch)).rejects.toMatchObject({ status: 409 });
  });
});

