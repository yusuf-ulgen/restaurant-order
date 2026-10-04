import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Button, EmptyState, PageHeader, Spinner, ConfirmationDialog } from '@restaurant-order/ui';
import type { CategoryContract, MenuContract, MenuItemContract, ModifierGroupContract } from '@restaurant-order/contracts';
import { useAuth } from '../auth/AuthContext';
import { useAdminConfig } from '../config/AdminConfigContext';
import { catalogApi, CatalogApiError } from './catalogApi';
import { getCatalogPermissions } from './catalogPermissions';
import { MenuSelector } from './MenuSelector';
import { CategoryList } from './CategoryList';
import { CategoryEditorSheet } from './CategoryEditorSheet';
import { MenuItemList } from './MenuItemList';
import { MenuItemEditorSheet, type ItemDraft } from './MenuItemEditorSheet';
import { CatalogPreviewSheet } from './CatalogPreviewSheet';
import './catalog.css';

export const MenuCatalogView: React.FC = () => {
  const { user } = useAuth();
  const { selectedBranchId, selectedBranch } = useAdminConfig();
  const permissions = getCatalogPermissions(user?.role);
  const branchAllowed = user?.role !== 'BranchManager' || (!!user.branchId && selectedBranchId === user.branchId);
  const [menus, setMenus] = useState<MenuContract[]>([]);
  const [selectedMenuId, setSelectedMenuId] = useState('');
  const [categories, setCategories] = useState<CategoryContract[]>([]);
  const [selectedCategoryId, setSelectedCategoryId] = useState('');
  const [items, setItems] = useState<MenuItemContract[]>([]);
  const [groups, setGroups] = useState<ModifierGroupContract[]>([]);
  const [availability, setAvailability] = useState<Awaited<ReturnType<typeof catalogApi.listAvailability>>>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isMutating, setIsMutating] = useState(false);
  const [error, setError] = useState<{ status?: number; message: string } | null>(null);
  const [success, setSuccess] = useState('');
  const [categorySheet, setCategorySheet] = useState(false);
  const [editingCategory, setEditingCategory] = useState<CategoryContract | null>(null);
  const [itemSheet, setItemSheet] = useState(false);
  const [editingItem, setEditingItem] = useState<MenuItemContract | null>(null);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [pendingMenu, setPendingMenu] = useState<{ menu: MenuContract; action: 'activate' | 'archive' } | null>(null);
  const requestId = useRef(0);
  const selectedMenuRef = useRef('');
  const selectedCategoryRef = useRef('');

  const recordError = useCallback((reason: unknown) => {
    if (reason instanceof CatalogApiError) setError({ status: reason.status, message: reason.message });
    else setError({ message: reason instanceof Error ? reason.message : 'Katalog isteği tamamlanamadı.' });
    setSuccess('');
  }, []);

  const loadCatalog = useCallback(async (preferredMenuId?: string) => {
    const branchId = selectedBranchId;
    const sequence = ++requestId.current;
    if (!branchId || !permissions.canView || !branchAllowed) {
      setMenus([]); setCategories([]); setItems([]); setGroups([]); setAvailability([]);
      setSelectedMenuId(''); selectedMenuRef.current = ''; selectedCategoryRef.current = ''; setIsLoading(false); return;
    }
    setIsLoading(true); setError(null); setSuccess('');
    try {
      const [menuList, availabilityList] = await Promise.all([
        catalogApi.listMenus(branchId), catalogApi.listAvailability(branchId),
      ]);
      if (sequence !== requestId.current) return;
      const targetMenuId = preferredMenuId && menuList.some((menu) => menu.id === preferredMenuId)
        ? preferredMenuId
        : menuList.some((menu) => menu.id === selectedMenuRef.current) ? selectedMenuRef.current : menuList[0]?.id ?? '';
      setMenus(menuList); setAvailability(availabilityList); setSelectedMenuId(targetMenuId); selectedMenuRef.current = targetMenuId;
      if (!targetMenuId) { setCategories([]); setItems([]); setGroups([]); setSelectedCategoryId(''); selectedCategoryRef.current = ''; return; }
      setCategories([]); setItems([]); setGroups([]);
      const [categoryList, itemList, groupList] = await Promise.all([
        catalogApi.listCategories(branchId, targetMenuId), catalogApi.listItems(branchId, targetMenuId), catalogApi.listModifierGroups(branchId),
      ]);
      if (sequence !== requestId.current) return;
      const currentCategoryId = categoryList.some((category) => category.id === selectedCategoryRef.current) ? selectedCategoryRef.current : categoryList.find((category) => category.isActive)?.id ?? '';
      setCategories(categoryList); setItems(itemList); setGroups(groupList); setSelectedCategoryId(currentCategoryId); selectedCategoryRef.current = currentCategoryId;
    } catch (reason) {
      if (sequence === requestId.current) recordError(reason);
    } finally {
      if (sequence === requestId.current) setIsLoading(false);
    }
  }, [selectedBranchId, permissions.canView, branchAllowed, recordError]);

  useEffect(() => {
    setMenus([]); setCategories([]); setItems([]); setGroups([]); setAvailability([]);
    setSelectedMenuId(''); setSelectedCategoryId(''); selectedMenuRef.current = ''; selectedCategoryRef.current = '';
    void loadCatalog();
    return () => { requestId.current += 1; };
  }, [selectedBranchId, user?.tenantId, branchAllowed, loadCatalog]);

  const mutate = async <T,>(work: () => Promise<T>, message: string, menuId?: string): Promise<T> => {
    setIsMutating(true); setError(null); setSuccess('');
    try {
      const result = await work();
      await loadCatalog(menuId ?? selectedMenuRef.current);
      setSuccess(message);
      return result;
    } catch (reason) { recordError(reason); throw reason; }
    finally { setIsMutating(false); }
  };

  const selectedMenu = menus.find((menu) => menu.id === selectedMenuId) ?? null;
  const selectedCategoryItems = items.filter((item) => !selectedCategoryId || item.categoryId === selectedCategoryId);
  const openCreateItem = () => { setEditingItem(null); setItemSheet(true); };
  const openEditItem = (item: MenuItemContract) => { setEditingItem(item); setItemSheet(true); };
  const openCreateCategory = () => { setEditingCategory(null); setCategorySheet(true); };

  const saveItem = async (draft: ItemDraft, item?: MenuItemContract) => {
    const core = {
      categoryId: draft.categoryId, name: draft.name, slug: draft.slug,
      shortDescription: draft.shortDescription || null, fullDescription: draft.fullDescription || null,
      imageUrl: draft.imageUrl || null, sortOrder: draft.sortOrder,
      ...(permissions.canManagePricing || !item ? { basePriceMinorUnits: draft.basePriceMinorUnits } : {}),
    };
    await mutate(async () => {
      const saved = await catalogApi.saveItem(selectedBranchId!, selectedMenuId, core, item);
      const metadataChanged = !item || item.spicyLevel !== draft.spicyLevel ||
        JSON.stringify(item.dietaryTags) !== JSON.stringify(draft.dietaryTags) || JSON.stringify(item.allergenTags) !== JSON.stringify(draft.allergenTags);
      if (metadataChanged) await catalogApi.updateMetadata(selectedBranchId!, selectedMenuId, saved, {
        spicyLevel: draft.spicyLevel, dietaryTags: draft.dietaryTags, allergenTags: draft.allergenTags,
      });
      return saved;
    }, 'Ürün kataloğa kaydedildi.');
  };

  const errorText = error?.status === 409
    ? `Kayıt başka bir kullanıcı tarafından değiştirildi. ${error.message} Güncel veriyi yükleyip değişikliklerinizi yeniden uygulayın.`
    : error?.status === 412
      ? `Güncelleme tokenı geçersiz veya eksik. Güncel kayıt ve token yüklenecek; değişikliklerinizi yeniden uygulayın. ${error.message}`
      : error?.status === 403
        ? `Bu işlem için yetkiniz yok. Sunucu erişimi reddetti. ${error.message}`
        : error?.message ?? '';

  if (!permissions.canView) return <div className="catalog-page"><PageHeader title="Menü kataloğu" /><p role="alert">Menü kataloğunu görüntüleme izniniz yok.</p></div>;
  if (!branchAllowed) return <div className="catalog-page"><PageHeader title="Menü kataloğu" /><p role="alert">Şube yöneticileri yalnızca atanmış şubelerinin kataloğunu yönetebilir.</p></div>;
  if (!selectedBranchId) return <div className="catalog-page"><PageHeader title="Menü kataloğu" /><EmptyState title="Şube seçin" description="Katalog yönetimi için bir şube seçilmelidir." /></div>;

  return <div className="catalog-page" data-testid="menu-catalog-view">
    <PageHeader title="Menü kataloğu" subtitle={`${selectedBranch?.name ?? 'Seçili şube'} için menü, kategori ve ürün yönetimi`} headingLevel={2} />
    {error && <div className="catalog-message catalog-error" role="alert">
      <p>{errorText}</p><div className="catalog-actions"><Button size="md" variant="outline" onClick={() => void loadCatalog(selectedMenuRef.current)}>Güncel veriyi yükle</Button>
        {error.status !== 403 && <Button size="md" variant="outline" onClick={() => void loadCatalog(selectedMenuRef.current)}>Tekrar dene</Button>}</div>
    </div>}
    {success && <p role="status" className="catalog-message catalog-success">{success}</p>}
    {isLoading ? <div role="status" className="catalog-panel"><Spinner label="Menü kataloğu yükleniyor…" /></div> : <div className="catalog-list">
      <MenuSelector menus={menus} selectedMenuId={selectedMenuId} canManage={permissions.canManage && !isMutating}
        onSelect={(id) => { selectedMenuRef.current = id; setSelectedMenuId(id); void loadCatalog(id); }}
        onCreate={async (data) => { const created = await mutate(() => catalogApi.createMenu(selectedBranchId, data), 'Menü oluşturuldu.'); selectedMenuRef.current = created.id; await loadCatalog(created.id); }}
        onEdit={async (menu, data) => { await mutate(() => catalogApi.updateMenu(selectedBranchId, menu, data), 'Menü güncellendi.'); }}
        onStatusChange={(menu, action) => setPendingMenu({ menu, action })} />
      {selectedMenu && <>
        <div className="catalog-toolbar"><Button size="md" variant="outline" onClick={() => setPreviewOpen(true)}>Önizle</Button>
          <span>{isMutating ? 'Kaydediliyor…' : 'Değişiklikler sunucuya kaydedilir.'}</span></div>
        <div className="catalog-grid">
          <section className="catalog-panel">
            <CategoryList categories={categories} selectedCategoryId={selectedCategoryId} canManage={permissions.canManage && !isMutating}
              onSelect={(id) => { selectedCategoryRef.current = id; setSelectedCategoryId(id); }} onEdit={(category) => { setEditingCategory(category); setCategorySheet(true); }} onCreate={openCreateCategory}
              onToggle={async (category) => { await mutate(() => catalogApi.setCategoryState(selectedBranchId, selectedMenu.id, category, !category.isActive), 'Kategori durumu güncellendi.'); }} />
          </section>
          <section className="catalog-panel">
            <MenuItemList items={selectedCategoryItems} availability={availability} canManage={permissions.canManage && !isMutating}
              canPrice={permissions.canManagePricing} canQuick86={permissions.canQuick86} onEdit={openEditItem} onCreate={openCreateItem}
              onToggleStatus={async (item) => { await mutate(() => catalogApi.setItemState(selectedBranchId, selectedMenu.id, item, !item.isActive), 'Ürün durumu güncellendi.'); }}
              onAvailabilityChanged={async () => { await loadCatalog(selectedMenu.id); setSuccess('Ürün stok durumu güncellendi.'); }} onError={recordError} />
          </section>
        </div>
      </>}
      {!selectedMenu && !isLoading && <div className="catalog-panel"><EmptyState title="Henüz menü yok" description="Bu şube için ilk menüyü oluşturarak başlayın." /></div>}
    </div>}
    <CategoryEditorSheet isOpen={categorySheet} category={editingCategory} categories={categories} onClose={() => setCategorySheet(false)}
      onSave={async (data, category) => { await mutate(() => catalogApi.saveCategory(selectedBranchId, selectedMenuId, data, category), 'Kategori kaydedildi.'); }} />
    <MenuItemEditorSheet isOpen={itemSheet} branchId={selectedBranchId} menuId={selectedMenuId} item={editingItem ? items.find((entry) => entry.id === editingItem.id) ?? editingItem : null}
      categories={categories} allItems={items} groups={groups} canManage={permissions.canManage} canPrice={permissions.canManagePricing}
      onClose={() => setItemSheet(false)} onSave={saveItem} onNestedSaved={() => loadCatalog(selectedMenuId)} onError={recordError} />
    <CatalogPreviewSheet isOpen={previewOpen} menu={selectedMenu} categories={categories} items={items} onClose={() => setPreviewOpen(false)} />
    <ConfirmationDialog isOpen={!!pendingMenu} title={pendingMenu?.action === 'archive' ? 'Menüyü arşivle' : 'Menüyü yayınla'}
      message={pendingMenu?.action === 'archive' ? 'Arşivlenen menüde yeni düzenleme yapılamaz.' : 'Menü aktif katalogda kullanıma açılacak.'}
      confirmLabel={pendingMenu?.action === 'archive' ? 'Arşivle' : 'Yayınla'} isLoading={isMutating}
      onCancel={() => setPendingMenu(null)} onConfirm={async () => {
        if (!pendingMenu) return;
        const pending = pendingMenu;
        try { await mutate(() => catalogApi.setMenuState(selectedBranchId, pending.menu, pending.action), 'Menü durumu güncellendi.'); setPendingMenu(null); }
        catch { /* Error is shown in the catalog alert. */ }
      }} />
  </div>;
};
