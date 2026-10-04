import { fetchWithCsrf } from '@restaurant-order/contracts';
import type {
  AvailabilityContract,
  CategoryContract,
  MenuContract,
  MenuItemContract,
  ModifierGroupContract,
  ModifierOptionContract,
  ProblemDetailsContract,
  VariantContract,
} from '@restaurant-order/contracts';

export class CatalogApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'CatalogApiError';
  }
}

async function request<T>(path: string, method = 'GET', body?: unknown, token?: string): Promise<T> {
  const headers = new Headers();
  if (token) headers.set('If-Match', `"${token}"`);
  const response = await fetchWithCsrf(path, {
    method,
    headers,
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as ProblemDetailsContract;
    const detail = response.status === 500
      ? 'Beklenmeyen bir sunucu hatası oluştu. Daha sonra yeniden deneyin.'
      : problem.detail || problem.title || `İstek başarısız (${response.status}).`;
    throw new CatalogApiError(response.status, detail);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

const base = (branchId: string) => `/api/v1/catalog/branches/${branchId}`;
const menuPath = (branchId: string, menuId: string) => `${base(branchId)}/menus/${menuId}`;
const itemPath = (branchId: string, menuId: string, itemId: string) => `${menuPath(branchId, menuId)}/items/${itemId}`;
const tokenBody = <T extends object>(data: T, token?: string) => ({ ...data, ...(token ? { concurrencyToken: token } : {}) });

export const catalogApi = {
  listMenus: (branchId: string) => request<MenuContract[]>(`${base(branchId)}/menus`),
  createMenu: (branchId: string, data: { name: string; slug: string; description: string; sortOrder: number }) =>
    request<MenuContract>(`${base(branchId)}/menus`, 'POST', data),
  updateMenu: (branchId: string, menu: MenuContract, data: { name: string; description: string; sortOrder: number }) =>
    request<MenuContract>(menuPath(branchId, menu.id), 'PUT', tokenBody(data, menu.concurrencyToken), menu.concurrencyToken),
  setMenuState: (branchId: string, menu: MenuContract, action: 'activate' | 'archive') =>
    request<MenuContract>(`${menuPath(branchId, menu.id)}/${action}`, 'POST', tokenBody({}, menu.concurrencyToken), menu.concurrencyToken),
  listCategories: (branchId: string, menuId: string) => request<CategoryContract[]>(`${menuPath(branchId, menuId)}/categories`),
  saveCategory: (branchId: string, menuId: string, data: { name: string; slug: string; description: string; sortOrder: number }, category?: CategoryContract) =>
    request<CategoryContract>(`${menuPath(branchId, menuId)}/categories${category ? `/${category.id}` : ''}`, category ? 'PUT' : 'POST', category ? tokenBody(data, category.concurrencyToken) : data, category?.concurrencyToken),
  setCategoryState: (branchId: string, menuId: string, category: CategoryContract, active: boolean) =>
    request<CategoryContract>(`${menuPath(branchId, menuId)}/categories/${category.id}/${active ? 'activate' : 'deactivate'}`, 'POST', tokenBody({}, category.concurrencyToken), category.concurrencyToken),
  listItems: (branchId: string, menuId: string, categoryId?: string) => {
    const query = categoryId ? `?categoryId=${encodeURIComponent(categoryId)}` : '';
    return request<MenuItemContract[]>(`${menuPath(branchId, menuId)}/items${query}`);
  },
  saveItem: (branchId: string, menuId: string, data: Record<string, unknown>, item?: MenuItemContract) =>
    request<MenuItemContract>(`${menuPath(branchId, menuId)}/items${item ? `/${item.id}` : ''}`, item ? 'PUT' : 'POST', item ? tokenBody(data, item.concurrencyToken) : data, item?.concurrencyToken),
  updateItemPrice: (branchId: string, menuId: string, item: MenuItemContract, basePriceMinorUnits: number) =>
    request<MenuItemContract>(`${itemPath(branchId, menuId, item.id)}/price`, 'PUT', tokenBody({ basePriceMinorUnits }, item.concurrencyToken), item.concurrencyToken),
  setItemState: (branchId: string, menuId: string, item: MenuItemContract, active: boolean) =>
    request<MenuItemContract>(`${itemPath(branchId, menuId, item.id)}/${active ? 'activate' : 'deactivate'}`, 'POST', tokenBody({}, item.concurrencyToken), item.concurrencyToken),
  updateMetadata: (branchId: string, menuId: string, item: MenuItemContract, data: { spicyLevel: number; dietaryTags: string[]; allergenTags: string[] }) =>
    request<MenuItemContract>(`${itemPath(branchId, menuId, item.id)}/metadata`, 'PUT', tokenBody(data, item.concurrencyToken), item.concurrencyToken),
  listVariants: (branchId: string, menuId: string, itemId: string) => request<VariantContract[]>(`${itemPath(branchId, menuId, itemId)}/variants`),
  saveVariant: (branchId: string, menuId: string, itemId: string, data: Record<string, unknown>, variant?: VariantContract) =>
    request<VariantContract>(`${itemPath(branchId, menuId, itemId)}/variants${variant ? `/${variant.id}` : ''}`, variant ? 'PUT' : 'POST', variant ? tokenBody(data, variant.concurrencyToken) : data, variant?.concurrencyToken),
  updateVariantPrice: (branchId: string, menuId: string, itemId: string, variant: VariantContract, price: number) =>
    request<VariantContract>(`${itemPath(branchId, menuId, itemId)}/variants/${variant.id}/price`, 'PUT', tokenBody({ absolutePriceMinorUnits: price }, variant.concurrencyToken), variant.concurrencyToken),
  listModifierGroups: (branchId: string) => request<ModifierGroupContract[]>(`${base(branchId)}/modifier-groups`),
  saveModifierGroup: (branchId: string, data: Record<string, unknown>, group?: ModifierGroupContract) =>
    request<ModifierGroupContract>(`${base(branchId)}/modifier-groups${group ? `/${group.id}` : ''}`, group ? 'PUT' : 'POST', group ? tokenBody(data, group.concurrencyToken) : data, group?.concurrencyToken),
  saveModifierOption: (branchId: string, groupId: string, data: Record<string, unknown>, option?: ModifierOptionContract) =>
    request<ModifierOptionContract>(`${base(branchId)}/modifier-groups/${groupId}/options${option ? `/${option.id}` : ''}`, option ? 'PUT' : 'POST', option ? tokenBody(data, option.concurrencyToken) : data, option?.concurrencyToken),
  updateModifierOptionPrice: (branchId: string, groupId: string, option: ModifierOptionContract, priceDeltaMinorUnits: number) =>
    request<ModifierOptionContract>(`${base(branchId)}/modifier-groups/${groupId}/options/${option.id}/price`, 'PUT', tokenBody({ priceDeltaMinorUnits }, option.concurrencyToken), option.concurrencyToken),
  assignModifierGroup: (branchId: string, menuId: string, item: MenuItemContract, modifierGroupId: string) =>
    request<MenuItemContract>(`${itemPath(branchId, menuId, item.id)}/modifier-groups`, 'POST', { modifierGroupId, concurrencyToken: item.concurrencyToken }, item.concurrencyToken),
  removeModifierGroup: (branchId: string, menuId: string, item: MenuItemContract, groupId: string) =>
    request<MenuItemContract>(`${itemPath(branchId, menuId, item.id)}/modifier-groups/${groupId}`, 'DELETE', undefined, item.concurrencyToken),
  listAvailability: (branchId: string) => request<AvailabilityContract[]>(`${base(branchId)}/availability`),
  setAvailability: (branchId: string, menuId: string, item: MenuItemContract, availability: AvailabilityContract | undefined, available: boolean) => {
    const action = available ? 'restock' : 'quick-86';
    const endpoint = `${itemPath(branchId, menuId, item.id)}/${action}`;
    const body = available
      ? { note: 'Stok yenilendi', concurrencyToken: availability?.concurrencyToken }
      : { reasonCode: 'Manual', note: 'Geçici olarak stokta yok', concurrencyToken: availability?.concurrencyToken };
    return request<AvailabilityContract>(endpoint, 'POST', body, availability?.concurrencyToken);
  },
};
