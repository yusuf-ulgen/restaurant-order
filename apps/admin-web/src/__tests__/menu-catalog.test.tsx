import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import type { UserPrincipalDto, MenuContract, CategoryContract, MenuItemContract } from '@restaurant-order/contracts';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider, createDefaultFallbackTheme } from '../config/AdminConfigContext';
import { MenuCatalogView } from '../catalog/MenuCatalogView';
import { MenuItemEditorSheet } from '../catalog/MenuItemEditorSheet';
import { getCatalogPermissions } from '../catalog/catalogPermissions';
import { dietaryAllergenConflict } from '../catalog/catalogValidation';
import { resolveAdminNavigation } from '../navigation/navigationRegistry';

const ids = { branch: 'branch-1', menu: 'menu-1', category: 'category-1', item: 'item-1', variant: 'variant-1' };
const menu: MenuContract = { id: ids.menu, tenantId: 'tenant-1', branchId: ids.branch, name: 'Akşam', slug: 'aksam', description: 'Günlük', status: 'Active', sortOrder: 0, concurrencyToken: 'menu-token' };
const category: CategoryContract = { id: ids.category, tenantId: 'tenant-1', branchId: ids.branch, menuId: ids.menu, name: 'Ana yemek', slug: 'ana-yemek', description: null, sortOrder: 0, isActive: true, concurrencyToken: 'category-token' };
const item: MenuItemContract = {
  id: ids.item, tenantId: 'tenant-1', branchId: ids.branch, menuId: ids.menu, categoryId: ids.category,
  name: 'Köfte', slug: 'kofte', shortDescription: 'Izgara', fullDescription: 'Günlük hazırlanır', imageUrl: null,
  basePriceMinorUnits: 12500, sortOrder: 0, isActive: true, spicyLevel: 0, dietaryTags: [], allergenTags: [],
  concurrencyToken: 'item-token', variants: [], modifierGroups: [],
};
const admin: UserPrincipalDto = { userId: 'admin', email: 'admin@example.test', role: 'RestaurantAdmin', tenantId: 'tenant-1', securityVersion: 1 };

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const defaultFetch = (url: string, init?: RequestInit) => {
  const method = (init?.method ?? 'GET').toUpperCase();
  if (url.endsWith('/menus') && method === 'GET') return json([menu]);
  if (url.endsWith('/availability')) return json([]);
  if (url.endsWith('/categories')) return json([category]);
  if (url.endsWith('/items') && method === 'GET') return json([item]);
  if (url.endsWith('/modifier-groups')) return json([]);
  if (method === 'POST' || method === 'PUT' || method === 'DELETE') return json(item);
  return json({});
};

function renderCatalog(user: UserPrincipalDto = admin, branchId = ids.branch) {
  return render(<AuthProvider initialUser={user}><AdminConfigProvider
    initialTheme={createDefaultFallbackTheme(user.tenantId, branchId)}
    initialBranches={[{ id: branchId, brandId: 'brand-1', name: 'Merkez', slug: 'merkez', isActive: true, status: 'Active' }]}
    initialBranchId={branchId}>
    <MenuCatalogView />
  </AdminConfigProvider></AuthProvider>);
}

describe('menu catalog management', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    vi.stubGlobal('fetch', vi.fn(defaultFetch));
  });

  it('loads menus, selects a menu, and lists its categories and products from API DTOs', async () => {
    renderCatalog();
    expect(await screen.findByText('Köfte')).toBeTruthy();
    expect((screen.getByLabelText('Menü') as HTMLSelectElement).value).toBe(ids.menu);
    expect(screen.getByText('Ana yemek')).toBeTruthy();
    expect(fetch).toHaveBeenCalledWith(`/api/v1/catalog/branches/${ids.branch}/menus`, expect.objectContaining({ method: 'GET' }));
  });

  it('creates and updates a category with the endpoint request shape and its concurrency token', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => { requests.push({ url, init }); return defaultFetch(url, init); }));
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(screen.getByRole('button', { name: 'Kategori ekle' }));
    fireEvent.change(screen.getByLabelText('Kategori adı'), { target: { value: 'Tatlı' } });
    fireEvent.change(screen.getByLabelText('Slug'), { target: { value: 'tatli' } });
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }));
    await screen.findByText('Kategori kaydedildi.');
    const create = requests.find((entry) => entry.url.endsWith('/categories') && entry.init?.method === 'POST');
    expect(JSON.parse(String(create?.init?.body))).toMatchObject({ name: 'Tatlı', slug: 'tatli', sortOrder: 1 });

    fireEvent.click(within(screen.getByRole('region', { name: 'Kategoriler' })).getByRole('button', { name: 'Düzenle' }));
    fireEvent.change(screen.getByLabelText('Kategori adı'), { target: { value: 'Ana yemekler' } });
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }));
    await waitFor(() => expect(requests.some((entry) => entry.url.endsWith(`/categories/${ids.category}`) && entry.init?.method === 'PUT')).toBe(true));
    const update = requests.find((entry) => entry.url.endsWith(`/categories/${ids.category}`) && entry.init?.method === 'PUT');
    expect(JSON.parse(String(update?.init?.body))).toMatchObject({ name: 'Ana yemekler', concurrencyToken: category.concurrencyToken });
    expect(new Headers(update?.init?.headers).get('If-Match')).toContain(category.concurrencyToken);
  });

  it('creates an item and then saves dietary metadata through the dedicated API', async () => {
    const created: MenuItemContract = { ...item, id: 'new-item', name: 'Mercimek', slug: 'mercimek', concurrencyToken: 'new-token' };
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      requests.push({ url, init });
      if (url.endsWith('/items') && init?.method === 'POST') return json(created, 201);
      if (url.endsWith('/metadata')) return json(created);
      return defaultFetch(url, init);
    }));
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(screen.getByRole('button', { name: 'Ürün ekle' }));
    fireEvent.change(screen.getByLabelText('Ürün adı'), { target: { value: 'Mercimek' } });
    fireEvent.change(screen.getByLabelText('Slug'), { target: { value: 'mercimek' } });
    fireEvent.change(screen.getByLabelText(/Temel fiyat \(/), { target: { value: '89.50' } });
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }));
    await screen.findByText('Ürün kataloğa kaydedildi.');
    const create = requests.find((entry) => entry.url.endsWith('/items') && entry.init?.method === 'POST');
    const metadata = requests.find((entry) => entry.url.endsWith('/metadata'));
    expect(JSON.parse(String(create?.init?.body))).toMatchObject({ categoryId: ids.category, basePriceMinorUnits: 8950 });
    expect(JSON.parse(String(metadata?.init?.body))).toMatchObject({ spicyLevel: 0, dietaryTags: [], allergenTags: [], concurrencyToken: 'new-token' });
  });

  it('updates an existing item with If-Match and the current token', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => { requests.push({ url, init }); return defaultFetch(url, init); }));
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(within(screen.getByRole('region', { name: 'Ürünler' })).getByRole('button', { name: 'Düzenle' }));
    fireEvent.change(screen.getByLabelText('Ürün adı'), { target: { value: 'Izgara köfte' } });
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }));
    await screen.findByText('Ürün kataloğa kaydedildi.');
    const update = requests.find((entry) => entry.url.endsWith(`/items/${ids.item}`) && entry.init?.method === 'PUT');
    expect(JSON.parse(String(update?.init?.body))).toMatchObject({ name: 'Izgara köfte', concurrencyToken: item.concurrencyToken });
    expect(new Headers(update?.init?.headers).get('If-Match')).toContain(item.concurrencyToken);
  });

  it('keeps the price field locked when a manager can edit catalog content without pricing permission', () => {
    render(<AuthProvider initialUser={admin}><AdminConfigProvider initialTheme={createDefaultFallbackTheme('tenant-1', ids.branch)}>
      <MenuItemEditorSheet isOpen branchId={ids.branch} menuId={ids.menu} item={item} categories={[category]} allItems={[item]} groups={[]}
        canManage canPrice={false} onClose={vi.fn()} onSave={vi.fn()} onNestedSaved={vi.fn()} onError={vi.fn()} />
    </AdminConfigProvider></AuthProvider>);
    expect((screen.getByLabelText(/Temel fiyat \(/) as HTMLInputElement).disabled).toBe(true);
    expect((screen.getByLabelText('Ürün adı') as HTMLInputElement).disabled).toBe(false);
  });

  it('updates item details with the current token and excludes protected pricing for callers without pricing permission', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => { requests.push({ url, init }); return defaultFetch(url, init); }));
    renderCatalog({ ...admin, role: 'SuperAdmin' });
    await screen.findByText('Köfte');
    expect(screen.queryByRole('button', { name: 'Düzenle' })).toBeNull();
    const permissions = getCatalogPermissions('BranchManager');
    expect(permissions).toMatchObject({ canView: true, canManage: true, canManagePricing: true, canQuick86: true });
    expect(getCatalogPermissions('SuperAdmin')).toMatchObject({ canView: true, canManage: false, canManagePricing: false, canQuick86: false });
    expect(resolveAdminNavigation(null, 'BranchManager').flatMap((section) => section.items).some((nav) => nav.id === 'menu')).toBe(true);
  });

  it('validates variant prices as integer minor units and persists modifier selection bounds', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => { requests.push({ url, init }); return defaultFetch(url, init); }));
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(within(screen.getByRole('region', { name: 'Ürünler' })).getByRole('button', { name: 'Düzenle' }));
    fireEvent.change(screen.getByLabelText('Varyant adı'), { target: { value: 'Büyük' } });
    fireEvent.change(screen.getByLabelText('Kod'), { target: { value: 'BIG' } });
    fireEvent.change(screen.getByLabelText(/Fiyat \(/), { target: { value: '175.25' } });
    fireEvent.click(screen.getByRole('button', { name: 'Varyantı kaydet' }));
    await waitFor(() => expect(requests.some((entry) => entry.url.endsWith('/variants') && entry.init?.method === 'POST')).toBe(true));
    const variantRequest = requests.find((entry) => entry.url.endsWith('/variants') && entry.init?.method === 'POST');
    expect(JSON.parse(String(variantRequest?.init?.body))).toMatchObject({ name: 'Büyük', code: 'BIG', absolutePriceMinorUnits: 17525 });

    fireEvent.change(screen.getByLabelText('Grup adı'), { target: { value: 'Ekstralar' } });
    fireEvent.change(screen.getByLabelText('En az seçim'), { target: { value: '2' } });
    fireEvent.change(screen.getByLabelText('En çok seçim'), { target: { value: '1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Grubu kaydet' }));
    expect((await screen.findByRole('alert')).textContent).toContain('0 ≤ minimum ≤ maksimum');
    expect(requests.some((entry) => entry.url.endsWith('/modifier-groups') && entry.init?.method === 'POST')).toBe(false);
  });

  it('rejects dietary and allergen contradictions before any metadata write', async () => {
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(within(screen.getByRole('region', { name: 'Ürünler' })).getByRole('button', { name: 'Düzenle' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Vegan' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Süt' }));
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }));
    expect((await screen.findByRole('alert')).textContent).toContain('Vegan etiketi');
    expect(dietaryAllergenConflict(['GlutenFree'], ['Gluten'])).toContain('Glutensiz');
  });

  it('confirms Quick-86 and restocks through the branch availability API', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => { requests.push({ url, init }); return defaultFetch(url, init); }));
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(screen.getByRole('button', { name: /86 · Stokta yok/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Stokta yok olarak işaretle' }));
    await screen.findByText('Ürün stok durumu güncellendi.');
    expect(requests.some((entry) => entry.url.endsWith('/quick-86') && entry.init?.method === 'POST')).toBe(true);
  });

  it.each([
    [403, 'Bu işlem için yetkiniz yok'],
    [409, 'başka bir kullanıcı tarafından değiştirildi'],
    [412, 'tokenı geçersiz veya eksik'],
    [500, 'Beklenmeyen bir sunucu hatası'],
  ])('explains API status %s and offers current data reload', async (status, expected) => {
    let fail = true;
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      if (url.endsWith('/categories') && init?.method === 'POST' && fail) return json({ detail: 'API failure detail' }, status);
      return defaultFetch(url, init);
    }));
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(screen.getByRole('button', { name: 'Kategori ekle' }));
    fireEvent.change(screen.getByLabelText('Kategori adı'), { target: { value: 'İçecek' } });
    fireEvent.change(screen.getByLabelText('Slug'), { target: { value: 'icecek' } });
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }));
    expect((await screen.findAllByText(new RegExp(expected, 'i'))).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Güncel veriyi yükle' })).toBeTruthy();
    fail = false;
  });

  it('shows loading, empty, error and retry states', async () => {
    let release: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn((url: string) => url.endsWith('/menus')
      ? new Promise<Response>((resolve) => { release = resolve; })
      : defaultFetch(url));
    vi.stubGlobal('fetch', fetchMock);
    renderCatalog();
    expect(screen.getByLabelText('Menü kataloğu yükleniyor…')).toBeTruthy();
    release?.(json([]));
    expect(await screen.findByText('Henüz menü yok')).toBeTruthy();

    let calls = 0;
    vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
      if (url.endsWith('/menus') && ++calls === 1) return json({ detail: 'internal raw detail' }, 500);
      return defaultFetch(url, init);
    }));
    const view = renderCatalog();
    expect(await screen.findByText('Beklenmeyen bir sunucu hatası oluştu. Daha sonra yeniden deneyin.')).toBeTruthy();
    expect(screen.queryByText('internal raw detail')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }));
    expect(await screen.findByText('Köfte')).toBeTruthy();
    view.unmount();
  });

  it('uses a mobile BottomSheet and exposes dialog labels and keyboard focus', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    renderCatalog();
    await screen.findByText('Köfte');
    fireEvent.click(screen.getByRole('button', { name: 'Kategori ekle' }));
    const dialog = screen.getByRole('dialog', { name: 'Kategori ekle' });
    expect(dialog).toBeTruthy();
    expect(within(dialog).getByLabelText('Kategori adı')).toBeTruthy();
    await waitFor(() => expect(dialog.contains(document.activeElement)).toBe(true));
  });

  it('switching branch clears the previous catalog before fetching new branch data', async () => {
    const calls: string[] = [];
    const fetchMock = vi.fn((url: string, init?: RequestInit) => {
      calls.push(url);
      if (url.endsWith('/menus')) return json(url.includes('branch-2') ? [{ ...menu, id: 'menu-2', name: 'Şube 2' }] : [menu]);
      if (url.endsWith('/categories')) return json(url.includes('menu-2') ? [] : [category]);
      if (url.endsWith('/items') && init?.method !== 'POST') return json(url.includes('menu-2') ? [] : [item]);
      return defaultFetch(url, init);
    });
    vi.stubGlobal('fetch', fetchMock);
    const view = renderCatalog();
    expect(await screen.findByText('Köfte')).toBeTruthy();
    view.unmount();
    const secondBranchUser = { ...admin, branchId: 'branch-2' };
    renderCatalog(secondBranchUser, 'branch-2');
    await waitFor(() => expect((screen.getByLabelText('Menü') as HTMLSelectElement).value).toBe('menu-2'));
    expect(calls.some((url) => url.includes('/branches/branch-2/menus'))).toBe(true);
    expect(screen.queryByText('Köfte')).toBeNull();
  });

  it('triggers category and item reordering via MenuCatalogView', async () => {
    const cat2: CategoryContract = { ...category, id: 'cat-2', name: 'Tatlılar', slug: 'tatlilar', sortOrder: 1 };
    const item2: MenuItemContract = { ...item, id: 'item-2', name: 'Baklava', slug: 'baklava', sortOrder: 1 };
    const reorderCalls: { url: string; body: unknown }[] = [];

    const fetchMock = vi.fn((url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase();
      if (url.endsWith('/menus') && method === 'GET') return json([menu]);
      if (url.endsWith('/categories') && method === 'GET') return json([category, cat2]);
      if (url.endsWith('/items') && method === 'GET') return json([item, item2]);
      if (url.endsWith('/availability')) return json([]);
      if (url.endsWith('/modifier-groups')) return json([]);
      if (url.includes('/reorder') && method === 'POST') {
        reorderCalls.push({ url, body: JSON.parse(init?.body as string) });
        return json(url.includes('/categories/') ? [cat2, category] : [item2, item]);
      }
      return json({});
    });
    vi.stubGlobal('fetch', fetchMock);

    renderCatalog();
    expect(await screen.findByText('Köfte')).toBeTruthy();
    expect(await screen.findByText('Baklava')).toBeTruthy();

    const downButtons = screen.getAllByRole('button', { name: 'Aşağı taşı' });
    expect(downButtons.length).toBeGreaterThan(1);
    fireEvent.click(downButtons[0]!);
    fireEvent.click(downButtons[downButtons.length - 2]!);

    await waitFor(() => {
      expect(reorderCalls.length).toBeGreaterThan(0);
    });
  });
});
