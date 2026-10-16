import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { RuntimeMenuReadModel, DraftCartLine } from '@restaurant-order/contracts';
import { verifyCartAgainstRuntimeMenu } from '../cart/cartValidator';
import { CartProvider } from '../cart/CartContext';
import { CartSheet } from '../components/cart/CartSheet';

describe('CartSheet & Validator Integration Tests', () => {
  const sampleRuntimeMenu: RuntimeMenuReadModel = {
    branchId: 'branch-1',
    currency: 'TRY',
    menus: [
      {
        id: 'menu-1',
        name: 'Ana Menü',
        slug: 'ana-menu',
        description: null,
        sortOrder: 1,
        categories: [
          {
            id: 'cat-1',
            name: 'Burgerler',
            slug: 'burgerler',
            description: null,
            sortOrder: 1,
            items: [
              {
                id: 'item-burger',
                name: 'Klasik Burger',
                slug: 'klasik-burger',
                shortDescription: 'Köfte, marul, domates',
                fullDescription: null,
                imageUrl: null,
                basePriceMinorUnits: 15000,
                sortOrder: 1,
                spicyLevel: 0,
                dietaryTags: [],
                allergenTags: ['Gluten'],
                isAvailable: true,
                expectedAvailableAtUtc: null,
                variants: [
                  {
                    id: 'var-single',
                    name: 'Tek Köfte',
                    code: 'SNG',
                    absolutePriceMinorUnits: 15000,
                    sortOrder: 1,
                    isDefault: true,
                    isAvailable: true,
                    expectedAvailableAtUtc: null,
                  },
                ],
                modifierGroups: [
                  {
                    id: 'group-sauce',
                    name: 'Sos',
                    minSelections: 0,
                    maxSelections: 2,
                    sortOrder: 1,
                    options: [
                      {
                        id: 'opt-mayo',
                        name: 'Mayonez',
                        priceDeltaMinorUnits: 0,
                        sortOrder: 1,
                        isDefault: true,
                      },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  };

  const sampleLine: DraftCartLine = {
    menuItemId: 'item-burger',
    variantId: 'var-single',
    selectedModifierOptionIds: ['opt-mayo'],
    customerNote: 'Tuzsuz olsun',
    quantity: 2,
    unitPriceMinorUnits: 15000,
    lineTotalMinorUnits: 30000,
    currency: 'TRY',
    configurationSignature: 'item-burger:var-single:opt-mayo:Tuzsuz olsun',
  };

  describe('verifyCartAgainstRuntimeMenu', () => {
    it('returns no issues for up-to-date cart lines', () => {
      const result = verifyCartAgainstRuntimeMenu([sampleLine], sampleRuntimeMenu);
      expect(result.issues).toHaveLength(0);
      expect(result.hasBlockingIssues).toBe(false);
      expect(result.hasPriceChanges).toBe(false);
    });

    it('detects when an item is removed from the runtime menu', () => {
      const removedLine: DraftCartLine = {
        ...sampleLine,
        menuItemId: 'item-deleted',
        configurationSignature: 'item-deleted:none::',
      };

      const result = verifyCartAgainstRuntimeMenu([removedLine], sampleRuntimeMenu);
      expect(result.issues).toHaveLength(1);
      expect(result.issues[0]?.type).toBe('item_removed');
      expect(result.hasBlockingIssues).toBe(true);
    });

    it('detects when an item becomes out of stock (86)', () => {
      const outOfStockMenu: RuntimeMenuReadModel = {
        ...sampleRuntimeMenu,
        menus: [
          {
            ...sampleRuntimeMenu.menus[0]!,
            categories: [
              {
                ...sampleRuntimeMenu.menus[0]!.categories[0]!,
                items: [
                  {
                    ...sampleRuntimeMenu.menus[0]!.categories[0]!.items[0]!,
                    isAvailable: false,
                  },
                ],
              },
            ],
          },
        ],
      };

      const result = verifyCartAgainstRuntimeMenu([sampleLine], outOfStockMenu);
      expect(result.issues).toHaveLength(1);
      expect(result.issues[0]?.type).toBe('item_unavailable');
      expect(result.hasBlockingIssues).toBe(true);
    });

    it('detects when a selected modifier option is removed from the menu', () => {
      const lineWithMissingModifier: DraftCartLine = {
        ...sampleLine,
        selectedModifierOptionIds: ['opt-removed'],
        configurationSignature: 'item-burger:var-single:opt-removed:',
      };

      const result = verifyCartAgainstRuntimeMenu([lineWithMissingModifier], sampleRuntimeMenu);
      expect(result.issues).toHaveLength(1);
      expect(result.issues[0]?.type).toBe('modifier_removed');
      expect(result.hasBlockingIssues).toBe(true);
    });

    it('detects price change without silent mutation and flags for customer review', () => {
      const priceUpdatedMenu: RuntimeMenuReadModel = {
        ...sampleRuntimeMenu,
        menus: [
          {
            ...sampleRuntimeMenu.menus[0]!,
            categories: [
              {
                ...sampleRuntimeMenu.menus[0]!.categories[0]!,
                items: [
                  {
                    ...sampleRuntimeMenu.menus[0]!.categories[0]!.items[0]!,
                    variants: [
                      {
                        ...sampleRuntimeMenu.menus[0]!.categories[0]!.items[0]!.variants[0]!,
                        absolutePriceMinorUnits: 18000, // increased from 15000
                      },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      };

      const result = verifyCartAgainstRuntimeMenu([sampleLine], priceUpdatedMenu);
      expect(result.issues).toHaveLength(1);
      expect(result.issues[0]?.type).toBe('price_changed');
      expect(result.issues[0]?.previousUnitPriceMinorUnits).toBe(15000);
      expect(result.issues[0]?.currentUnitPriceMinorUnits).toBe(18000);
      expect(result.hasPriceChanges).toBe(true);
      expect(result.hasBlockingIssues).toBe(false);
    });
  });

  describe('CartSheet Component', () => {
    beforeEach(() => {
      window.sessionStorage.clear();
    });

    it('renders empty cart state and allows navigation to menu', () => {
      const handleClose = vi.fn();
      const handleNavigateToMenu = vi.fn();

      render(
        <CartProvider sessionId="test-session">
          <CartSheet
            isOpen={true}
            onClose={handleClose}
            runtimeMenu={sampleRuntimeMenu}
            onNavigateToMenu={handleNavigateToMenu}
          />
        </CartProvider>
      );

      expect(screen.getByTestId('cart-empty-state')).toBeDefined();
      expect(screen.getByText('Sepetiniz Henüz Boş')).toBeDefined();

      const browseBtn = screen.getByTestId('cart-browse-menu-btn');
      fireEvent.click(browseBtn);

      expect(handleClose).toHaveBeenCalled();
      expect(handleNavigateToMenu).toHaveBeenCalled();
    });

    it('renders cart line items, subtotal, and handles quantity changes', () => {
      // Pre-seed sessionStorage with a line item
      const preseededDraft = {
        version: 1,
        sessionId: 'test-session',
        currency: 'TRY',
        lines: [sampleLine],
        savedAt: Date.now(),
      };
      window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

      render(
        <CartProvider sessionId="test-session">
          <CartSheet
            isOpen={true}
            onClose={vi.fn()}
            runtimeMenu={sampleRuntimeMenu}
            tableInfo={{ tableNumber: '5', tableName: 'Salon' }}
          />
        </CartProvider>
      );

      expect(screen.getByTestId('cart-subtotal-display').textContent).toContain('300,00');

      // Increment quantity
      const incrementBtn = screen.getByTestId(`cart-increment-${sampleLine.configurationSignature}`);
      fireEvent.click(incrementBtn);

      // Quantity becomes 3 -> subtotal becomes 450,00 TL
      expect(screen.getByTestId('cart-subtotal-display').textContent).toContain('450,00');

      // Decrement quantity
      const decrementBtn = screen.getByTestId(`cart-decrement-${sampleLine.configurationSignature}`);
      fireEvent.click(decrementBtn);

      expect(screen.getByTestId('cart-subtotal-display').textContent).toContain('300,00');
    });

    it('requires confirmation to delete line item', () => {
      const preseededDraft = {
        version: 1,
        sessionId: 'test-session',
        currency: 'TRY',
        lines: [sampleLine],
        savedAt: Date.now(),
      };
      window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

      render(
        <CartProvider sessionId="test-session">
          <CartSheet
            isOpen={true}
            onClose={vi.fn()}
            runtimeMenu={sampleRuntimeMenu}
          />
        </CartProvider>
      );

      const removeBtn = screen.getByTestId(`remove-line-${sampleLine.configurationSignature}`);
      fireEvent.click(removeBtn);

      expect(screen.getByText('Silinsin mi?')).toBeDefined();

      const confirmBtn = screen.getByTestId(`confirm-delete-${sampleLine.configurationSignature}`);
      fireEvent.click(confirmBtn);

      // Cart becomes empty
      expect(screen.getByTestId('cart-empty-state')).toBeDefined();
    });

    it('enforces Phase 8 boundary: CTA is disabled with informative copy and no mock order ID', () => {
      const preseededDraft = {
        version: 1,
        sessionId: 'test-session',
        currency: 'TRY',
        lines: [sampleLine],
        savedAt: Date.now(),
      };
      window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

      render(
        <CartProvider sessionId="test-session">
          <CartSheet
            isOpen={true}
            onClose={vi.fn()}
            runtimeMenu={sampleRuntimeMenu}
          />
        </CartProvider>
      );

      const ctaBtn = screen.getByTestId('submit-order-cta-btn') as HTMLButtonElement;
      expect(ctaBtn.disabled).toBe(true);

      const notice = screen.getByTestId('phase8-order-notice');
      expect(notice.textContent).toContain('Faz 8 kapsamında');
    });
  });
});
