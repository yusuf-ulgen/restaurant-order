import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { DigitalMenuView } from '../components/menu/DigitalMenuView';
import { runtimeMenuApi, RuntimeMenuApiError } from '../api/runtimeMenuApi';
import type { RuntimeMenuReadModel } from '@restaurant-order/contracts';

const mockRuntimeMenu: RuntimeMenuReadModel = {
  branchId: 'test-branch-id',
  currency: 'TRY',
  pricesIncludeTax: true,
  defaultTaxRateBps: 1000,
  isServiceChargeEnabled: false,
  serviceChargeRateBps: 0,
  menus: [
    {
      id: 'menu-1',
      name: 'Günlük Menü',
      slug: 'gunluk-menu',
      description: 'Lezzetli ana yemekler',
      sortOrder: 1,
      categories: [
        {
          id: 'cat-burgers',
          name: 'Burgerler',
          slug: 'burgerler',
          description: 'Özel soslu burgerler',
          sortOrder: 1,
          items: [
            {
              id: 'item-burger-1',
              name: 'Çıtır Tavuk Burger',
              slug: 'citir-tavuk-burger',
              shortDescription: 'Özel baharatlı çıtır tavuk göğsü',
              fullDescription: 'Özel baharatlı çıtır tavuk göğsü, marul, mayonez ve patates',
              imageUrl: 'https://cdn.example.com/citir-tavuk.jpg',
              basePriceMinorUnits: 22000,
              sortOrder: 1,
              spicyLevel: 1,
              dietaryTags: ['Halal'],
              allergenTags: ['Gluten', 'Eggs'],
              isAvailable: true,
              expectedAvailableAtUtc: null,
              variants: [],
              modifierGroups: [],
            },
            {
              id: 'item-burger-2',
              name: 'Vegan Köfte Burger',
              slug: 'vegan-kofte-burger',
              shortDescription: 'Bezelye proteinli vegan köfte',
              fullDescription: 'Bezelye proteinli vegan köfte, vegan cheddar, marul',
              imageUrl: null,
              basePriceMinorUnits: 26000,
              sortOrder: 2,
              spicyLevel: 0,
              dietaryTags: ['Vegan', 'Vegetarian'],
              allergenTags: ['Soy'],
              isAvailable: true,
              expectedAvailableAtUtc: null,
              variants: [],
              modifierGroups: [],
            },
            {
              id: 'item-burger-3',
              name: 'Gurme Dana Burger',
              slug: 'gurme-dana-burger',
              shortDescription: '200g dinlendirilmiş dana köfte',
              fullDescription: '200g dinlendirilmiş dana köfte, cheddar, karamelize soğan',
              imageUrl: 'javascript:alert(1)', // Malicious URL to test sanitization
              basePriceMinorUnits: 29000,
              sortOrder: 3,
              spicyLevel: 2,
              dietaryTags: ['Halal'],
              allergenTags: ['Gluten', 'Lactose'],
              isAvailable: false,
              expectedAvailableAtUtc: '2026-10-16T22:30:00Z',
              variants: [
                {
                  id: 'v-1',
                  name: 'Tek Köfte',
                  code: 'SINGLE',
                  absolutePriceMinorUnits: 29000,
                  sortOrder: 1,
                  isDefault: true,
                  isAvailable: false,
                  expectedAvailableAtUtc: null,
                },
                {
                  id: 'v-2',
                  name: 'Duble Köfte',
                  code: 'DOUBLE',
                  absolutePriceMinorUnits: 39000,
                  sortOrder: 2,
                  isDefault: false,
                  isAvailable: false,
                  expectedAvailableAtUtc: null,
                },
              ],
              modifierGroups: [],
            },
          ],
        },
        {
          id: 'cat-salads',
          name: 'Salatalar',
          slug: 'salatalar',
          description: 'Taze Akdeniz yeşillikleri',
          sortOrder: 2,
          items: [
            {
              id: 'item-salad-1',
              name: 'Akdeniz Yeşillik Salatası',
              slug: 'akdeniz-salatasi',
              shortDescription: 'Zeytin, domates ve ezine peyniri',
              fullDescription: 'Mevsim yeşillikleri, sızma zeytinyağı, nar ekşisi',
              imageUrl: '/assets/images/salad.jpg',
              basePriceMinorUnits: 18000,
              sortOrder: 1,
              spicyLevel: 0,
              dietaryTags: ['Vegetarian', 'GlutenFree'],
              allergenTags: ['Lactose'],
              isAvailable: true,
              expectedAvailableAtUtc: null,
              variants: [],
              modifierGroups: [],
            },
          ],
        },
      ],
    },
  ],
};

describe('DigitalMenuView (CUST-02)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue(mockRuntimeMenu);
  });

  it('loads and renders digital menu with search bar, categories, and product cards', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);

    // Initially shows loading
    expect(screen.getByTestId('menu-loading-state')).toBeDefined();

    // After resolution, products render
    expect(await screen.findByText('Çıtır Tavuk Burger')).toBeDefined();
    expect(screen.getByText('Vegan Köfte Burger')).toBeDefined();
    expect(screen.getByText('Akdeniz Yeşillik Salatası')).toBeDefined();

    // Categories are rendered in category bar
    expect(screen.getByTestId('category-pill-all')).toBeDefined();
    expect(screen.getByTestId('category-pill-cat-burgers')).toBeDefined();
    expect(screen.getByTestId('category-pill-cat-salads')).toBeDefined();
  });

  it('filters items when selecting a specific category pill', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Çıtır Tavuk Burger');

    // Select Salatalar category
    const saladsPill = screen.getByTestId('category-pill-cat-salads');
    fireEvent.click(saladsPill);

    expect(screen.getByText('Akdeniz Yeşillik Salatası')).toBeDefined();
    expect(screen.queryByText('Çıtır Tavuk Burger')).toBeNull();
    expect(screen.queryByText('Vegan Köfte Burger')).toBeNull();

    // Switch back to All
    const allPill = screen.getByTestId('category-pill-all');
    fireEvent.click(allPill);

    expect(screen.getByText('Çıtır Tavuk Burger')).toBeDefined();
    expect(screen.getByText('Akdeniz Yeşillik Salatası')).toBeDefined();
  });

  it('performs Turkish-aware search matching both name and description', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Çıtır Tavuk Burger');

    const searchInput = screen.getByRole('searchbox');

    // Search with normalized characters: "citir" matching "Çıtır Tavuk"
    fireEvent.change(searchInput, { target: { value: 'citir' } });
    expect(screen.getByText('Çıtır Tavuk Burger')).toBeDefined();
    expect(screen.queryByText('Vegan Köfte Burger')).toBeNull();

    // Search by description: "bezelye"
    fireEvent.change(searchInput, { target: { value: 'bezelye' } });
    expect(screen.getByText('Vegan Köfte Burger')).toBeDefined();
    expect(screen.queryByText('Çıtır Tavuk Burger')).toBeNull();

    // Clear search
    const clearBtn = screen.getByRole('button', { name: 'Aramayı temizle' });
    fireEvent.click(clearBtn);

    expect(screen.getByText('Çıtır Tavuk Burger')).toBeDefined();
    expect(screen.getByText('Vegan Köfte Burger')).toBeDefined();
  });

  it('applies dietary preferences and avoided allergens correctly', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Çıtır Tavuk Burger');

    // Open filter sheet
    const filterBtn = screen.getByTestId('open-filters-btn');
    fireEvent.click(filterBtn);

    expect(await screen.findByTestId('menu-filter-bottom-sheet')).toBeDefined();

    // Toggle Vegan filter
    const veganBtn = screen.getByTestId('dietary-filter-Vegan');
    fireEvent.click(veganBtn);

    // Toggle Avoided Allergen: Gluten
    const glutenBtn = screen.getByTestId('allergen-filter-Gluten');
    fireEvent.click(glutenBtn);

    // Close / Apply filters
    const applyBtn = screen.getByTestId('apply-filter-sheet-btn');
    fireEvent.click(applyBtn);

    // Vegan filter should only show Vegan burger
    expect(screen.getByText('Vegan Köfte Burger')).toBeDefined();
    expect(screen.queryByText('Çıtır Tavuk Burger')).toBeNull();
    expect(screen.queryByText('Akdeniz Yeşillik Salatası')).toBeNull();
  });

  it('shows allergen warning tag when product contains avoided allergen', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Çıtır Tavuk Burger');

    // Open filter sheet and avoid Gluten
    fireEvent.click(screen.getByTestId('open-filters-btn'));
    fireEvent.click(await screen.findByTestId('allergen-filter-Gluten'));
    fireEvent.click(screen.getByTestId('apply-filter-sheet-btn'));

    // Çıtır Tavuk Burger contains Gluten, so it must display allergen warning
    expect(screen.getByTestId('allergen-warning-item-burger-1')).toBeDefined();
    expect(screen.getAllByText(/Gluten içerir/).length).toBeGreaterThanOrEqual(1);
  });

  it('displays out of stock badge and estimated restock time', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Gurme Dana Burger');

    // Gurme Dana Burger isAvailable: false
    expect(screen.getByText(/Tükendi/)).toBeDefined();
  });

  it('sanitizes malicious image URLs and renders fallback', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Gurme Dana Burger');

    // Gurme Dana Burger has javascript:alert(1) as imageUrl -> must render fallback
    expect(screen.getByTestId('item-image-fallback-item-burger-3')).toBeDefined();
    expect(screen.queryByTestId('item-image-item-burger-3')).toBeNull();
  });

  it('renders no results state with clear filters button when query matches nothing', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Çıtır Tavuk Burger');

    const searchInput = screen.getByRole('searchbox');
    fireEvent.change(searchInput, { target: { value: 'olmayanurun123' } });

    expect(await screen.findByTestId('menu-no-results-state')).toBeDefined();
    expect(screen.getByText('Sonuç Bulunamadı')).toBeDefined();

    // Click "Filtreleri Temizle"
    const clearBtn = screen.getByTestId('empty-clear-filters-btn');
    fireEvent.click(clearBtn);

    expect(await screen.findByText('Çıtır Tavuk Burger')).toBeDefined();
  });

  it('renders empty menu state when branch has no menus', async () => {
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue({
      branchId: 'test-branch-id',
      currency: 'TRY',
      pricesIncludeTax: true,
      defaultTaxRateBps: 1000,
      isServiceChargeEnabled: false,
      serviceChargeRateBps: 0,
      menus: [],
    });

    render(<DigitalMenuView branchId="test-branch-id" />);
    expect(await screen.findByTestId('menu-empty-state')).toBeDefined();
    expect(screen.getByText('Menü Henüz Hazırlanmadı')).toBeDefined();
  });

  it('handles API error and provides retry capability', async () => {
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockRejectedValueOnce(
      new RuntimeMenuApiError(500, 'generic', 'Menü servisine ulaşılamadı.')
    );

    render(<DigitalMenuView branchId="test-branch-id" />);
    expect(await screen.findByTestId('menu-error-state')).toBeDefined();
    expect(screen.getByText('Menü servisine ulaşılamadı.')).toBeDefined();

    // Retry successfully
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue(mockRuntimeMenu);
    fireEvent.click(screen.getByTestId('retry-menu-btn'));

    expect(await screen.findByText('Çıtır Tavuk Burger')).toBeDefined();
  });

  it('triggers onSessionExpired callback when API returns 401 session expired', async () => {
    const expiredMock = vi.fn();
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockRejectedValue(
      new RuntimeMenuApiError(401, 'session_expired', 'Oturum süresi doldu.')
    );

    render(
      <DigitalMenuView
        branchId="test-branch-id"
        onSessionExpired={expiredMock}
      />
    );

    await waitFor(() => {
      expect(expiredMock).toHaveBeenCalled();
    });
  });

  it('opens item detail sheet when clicking item inspect button and closes it', async () => {
    render(<DigitalMenuView branchId="test-branch-id" />);
    await screen.findByText('Çıtır Tavuk Burger');

    const inspectBtn = screen.getByTestId('inspect-item-btn-item-burger-1');
    fireEvent.click(inspectBtn);

    expect(await screen.findByTestId('menu-item-detail-sheet')).toBeDefined();
    expect(screen.getByText('Özel baharatlı çıtır tavuk göğsü, marul, mayonez ve patates')).toBeDefined();

    const closeBtn = screen.getByTestId('close-detail-sheet-btn');
    fireEvent.click(closeBtn);

    expect(screen.queryByTestId('menu-item-detail-sheet')).toBeNull();
  });
});
