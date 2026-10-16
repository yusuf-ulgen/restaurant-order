import React, { useState, useEffect, useCallback, useMemo } from 'react';
import type { RuntimeMenuReadModel, RuntimeMenuItem } from '@restaurant-order/contracts';
import { Card, EmptyState, Button, Spinner } from '@restaurant-order/ui';
import { runtimeMenuApi, RuntimeMenuApiError } from '../../api/runtimeMenuApi';
import { normalizeTurkishText } from '../../utils/menuUtils';
import { MenuCategoryBar, CategoryItem } from './MenuCategoryBar';
import { MenuSearchAndFilterBar } from './MenuSearchAndFilterBar';
import { MenuFilterBottomSheet } from './MenuFilterBottomSheet';
import { MenuItemCard } from './MenuItemCard';
import { MenuItemDetailSheet } from './MenuItemDetailSheet';

export interface DigitalMenuViewProps {
  branchId: string;
  tableInfo?: { tableNumber: string; tableName: string };
  onSessionExpired?: () => void;
  onCallWaiter?: () => void;
  initialMenuData?: RuntimeMenuReadModel | null;
}

export const DigitalMenuView: React.FC<DigitalMenuViewProps> = ({
  branchId,
  tableInfo,
  onSessionExpired,
  onCallWaiter,
  initialMenuData = null,
}) => {
  const [menuData, setMenuData] = useState<RuntimeMenuReadModel | null>(initialMenuData);
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>(
    initialMenuData ? 'success' : 'loading'
  );
  const [error, setError] = useState<RuntimeMenuApiError | null>(null);

  // Filters & State
  const [searchQuery, setSearchQuery] = useState('');
  const [activeCategoryId, setActiveCategoryId] = useState<string | null>(null);
  const [selectedDietaryTags, setSelectedDietaryTags] = useState<string[]>([]);
  const [avoidedAllergens, setAvoidedAllergens] = useState<string[]>([]);
  const [isFilterSheetOpen, setIsFilterSheetOpen] = useState(false);
  const [selectedDetailItem, setSelectedDetailItem] = useState<RuntimeMenuItem | null>(null);

  const fetchMenu = useCallback(async (signal?: AbortSignal) => {
    setStatus('loading');
    setError(null);
    try {
      const data = await runtimeMenuApi.getRuntimeMenu(branchId, signal);
      setMenuData(data);
      setStatus('success');
    } catch (err: unknown) {
      if (err instanceof RuntimeMenuApiError) {
        if (err.errorType === 'session_expired') {
          onSessionExpired?.();
          return;
        }
        setError(err);
      } else {
        setError(new RuntimeMenuApiError(0, 'generic', 'Menü yüklenirken bir hata oluştu.'));
      }
      setStatus('error');
    }
  }, [branchId, onSessionExpired]);

  useEffect(() => {
    if (initialMenuData) return;
    const controller = new AbortController();
    fetchMenu(controller.signal);
    return () => {
      controller.abort();
    };
  }, [fetchMenu, initialMenuData]);

  // Aggregate categories and items
  const allCategories: CategoryItem[] = useMemo(() => {
    if (!menuData?.menus) return [];
    const list: CategoryItem[] = [];
    const seen = new Set<string>();

    menuData.menus.forEach((menu) => {
      (menu.categories || []).forEach((cat) => {
        if (!seen.has(cat.id)) {
          seen.add(cat.id);
          list.push({
            id: cat.id,
            name: cat.name,
            itemCount: (cat.items || []).length,
          });
        }
      });
    });

    return list;
  }, [menuData]);

  const allItemsWithCategory = useMemo(() => {
    if (!menuData?.menus) return [];
    const itemsList: { item: RuntimeMenuItem; categoryId: string }[] = [];
    menuData.menus.forEach((menu) => {
      (menu.categories || []).forEach((cat) => {
        (cat.items || []).forEach((item) => {
          itemsList.push({ item, categoryId: cat.id });
        });
      });
    });
    return itemsList;
  }, [menuData]);

  // Filtered items
  const filteredItems = useMemo(() => {
    const normalizedQuery = normalizeTurkishText(searchQuery);

    return allItemsWithCategory.filter(({ item, categoryId }) => {
      // Category filter
      if (activeCategoryId !== null && categoryId !== activeCategoryId) {
        return false;
      }

      // Search query filter (matches name, shortDescription, or fullDescription)
      if (normalizedQuery) {
        const normalizedName = normalizeTurkishText(item.name);
        const normalizedShort = normalizeTurkishText(item.shortDescription || '');
        const normalizedFull = normalizeTurkishText(item.fullDescription || '');

        const matches =
          normalizedName.includes(normalizedQuery) ||
          normalizedShort.includes(normalizedQuery) ||
          normalizedFull.includes(normalizedQuery);

        if (!matches) return false;
      }

      // Dietary tags filter (all selected dietary tags must be met)
      if (selectedDietaryTags.length > 0) {
        const itemTags = (item.dietaryTags || []).map((t) => t.toLowerCase());
        const hasAllTags = selectedDietaryTags.every((req) =>
          itemTags.includes(req.toLowerCase())
        );
        if (!hasAllTags) return false;
      }

      return true;
    }).map(({ item }) => item);
  }, [allItemsWithCategory, activeCategoryId, searchQuery, selectedDietaryTags]);

  const activeFilterCount = selectedDietaryTags.length + avoidedAllergens.length;
  const hasActiveFilters = Boolean(searchQuery || activeFilterCount > 0 || activeCategoryId !== null);

  const handleClearAllFilters = () => {
    setSearchQuery('');
    setActiveCategoryId(null);
    setSelectedDietaryTags([]);
    setAvoidedAllergens([]);
  };

  const handleToggleDietaryTag = (tag: string) => {
    setSelectedDietaryTags((prev) =>
      prev.includes(tag) ? prev.filter((t) => t !== tag) : [...prev, tag]
    );
  };

  const handleToggleAllergen = (allergen: string) => {
    setAvoidedAllergens((prev) =>
      prev.includes(allergen) ? prev.filter((a) => a !== allergen) : [...prev, allergen]
    );
  };

  const currency = menuData?.currency || 'TRY';

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        width: '100%',
        maxWidth: '1024px',
        margin: '0 auto',
        padding: 'var(--ro-space-4)',
        boxSizing: 'border-box',
      }}
      data-testid="digital-menu-view"
    >
      {/* Branch & Table Banner */}
      {tableInfo && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            marginBottom: 'var(--ro-space-3)',
            padding: 'var(--ro-space-2) var(--ro-space-3)',
            backgroundColor: 'var(--ro-color-surface)',
            border: '1px solid var(--ro-color-border)',
            borderRadius: 'var(--ro-radius-md)',
            fontSize: 'var(--ro-font-size-xs)',
          }}
          data-testid="menu-table-banner"
        >
          <div>
            <strong>Masa {tableInfo.tableNumber}</strong> • {tableInfo.tableName}
          </div>
          {onCallWaiter && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={onCallWaiter}
              data-testid="menu-call-waiter-btn"
              style={{ minHeight: '36px' }}
            >
              Garson Çağır
            </Button>
          )}
        </div>
      )}

      {/* Search and Filter Bar */}
      <MenuSearchAndFilterBar
        searchQuery={searchQuery}
        onSearchChange={setSearchQuery}
        activeFilterCount={activeFilterCount}
        onOpenFilters={() => setIsFilterSheetOpen(true)}
        onClearFilters={handleClearAllFilters}
        hasActiveFilters={hasActiveFilters}
      />

      {/* Category Pills Bar */}
      {allCategories.length > 0 && (
        <MenuCategoryBar
          categories={allCategories}
          activeCategoryId={activeCategoryId}
          onSelectCategory={setActiveCategoryId}
          totalItemCount={allItemsWithCategory.length}
        />
      )}

      {/* Content Rendering by State */}
      {status === 'loading' ? (
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            padding: 'var(--ro-space-8) var(--ro-space-4)',
            gap: 'var(--ro-space-3)',
          }}
          data-testid="menu-loading-state"
        >
          <Spinner size="lg" />
          <p style={{ color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-sm)' }}>
            Menü lezzetleri yükleniyor...
          </p>
        </div>
      ) : status === 'error' ? (
        <div
          style={{
            padding: 'var(--ro-space-6) 0',
          }}
          data-testid="menu-error-state"
        >
          <Card padding="lg">
            <EmptyState
              title="Menü Yüklenemedi"
              description={error?.message || 'Menü bilgileri alınırken bir problem oluştu.'}
            />
            {error?.retryAfterSeconds && (
              <p
                style={{
                  textAlign: 'center',
                  fontSize: 'var(--ro-font-size-xs)',
                  color: 'var(--ro-color-warning-text, #92400e)',
                  marginTop: 'var(--ro-space-2)',
                }}
              >
                Lütfen {error.retryAfterSeconds} saniye sonra tekrar deneyin.
              </p>
            )}
            <div
              style={{
                display: 'flex',
                justifyContent: 'center',
                marginTop: 'var(--ro-space-4)',
              }}
            >
              <Button
                type="button"
                variant="primary"
                size="md"
                onClick={() => fetchMenu()}
                data-testid="retry-menu-btn"
                style={{ minHeight: '44px' }}
              >
                Yeniden Dene
              </Button>
            </div>
          </Card>
        </div>
      ) : allItemsWithCategory.length === 0 ? (
        <div style={{ padding: 'var(--ro-space-6) 0' }} data-testid="menu-empty-state">
          <Card padding="lg">
            <EmptyState
              title="Menü Henüz Hazırlanmadı"
              description="Bu şubede henüz yayında olan bir menü bulunmamaktadır. Lütfen servis görevlisine danışınız."
            />
          </Card>
        </div>
      ) : filteredItems.length === 0 ? (
        <div style={{ padding: 'var(--ro-space-6) 0' }} data-testid="menu-no-results-state">
          <Card padding="lg">
            <EmptyState
              title="Sonuç Bulunamadı"
              description="Arama kriterlerinize veya seçili filtrelerinize uygun ürün bulunamadı. Filtreleri temizleyerek tekrar deneyebilirsiniz."
            />
            <div
              style={{
                display: 'flex',
                justifyContent: 'center',
                marginTop: 'var(--ro-space-4)',
              }}
            >
              <Button
                type="button"
                variant="outline"
                size="md"
                onClick={handleClearAllFilters}
                data-testid="empty-clear-filters-btn"
                style={{ minHeight: '44px' }}
              >
                Filtreleri Temizle
              </Button>
            </div>
          </Card>
        </div>
      ) : (
        <>
          {/* Out of stock category alert if all matching items are out of stock */}
          {filteredItems.every((item) => !item.isAvailable) && (
            <div
              role="alert"
              data-testid="all-items-out-of-stock-alert"
              style={{
                marginBottom: 'var(--ro-space-3)',
                padding: 'var(--ro-space-3)',
                backgroundColor: 'var(--ro-color-warning-subtle, #fef3c7)',
                color: 'var(--ro-color-warning-text, #92400e)',
                borderRadius: 'var(--ro-radius-md)',
                fontSize: 'var(--ro-font-size-xs)',
                fontWeight: 'var(--ro-font-weight-medium)',
              }}
            >
              Bu kategorideki tüm lezzetler şu an için tükenmiştir.
            </div>
          )}

          {/* Product Grid */}
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
              gap: 'var(--ro-space-3)',
            }}
            data-testid="menu-products-grid"
          >
            {filteredItems.map((item) => (
              <MenuItemCard
                key={item.id}
                item={item}
                currency={currency}
                avoidedAllergens={avoidedAllergens}
                onSelect={setSelectedDetailItem}
              />
            ))}
          </div>
        </>
      )}

      {/* Filter Bottom Sheet */}
      <MenuFilterBottomSheet
        isOpen={isFilterSheetOpen}
        onClose={() => setIsFilterSheetOpen(false)}
        selectedDietaryTags={selectedDietaryTags}
        onToggleDietaryTag={handleToggleDietaryTag}
        avoidedAllergens={avoidedAllergens}
        onToggleAllergen={handleToggleAllergen}
        onResetFilters={() => {
          setSelectedDietaryTags([]);
          setAvoidedAllergens([]);
        }}
      />

      {/* Item Detail Sheet */}
      <MenuItemDetailSheet
        item={selectedDetailItem}
        currency={currency}
        isOpen={selectedDetailItem !== null}
        onClose={() => setSelectedDetailItem(null)}
        avoidedAllergens={avoidedAllergens}
      />
    </div>
  );
};
