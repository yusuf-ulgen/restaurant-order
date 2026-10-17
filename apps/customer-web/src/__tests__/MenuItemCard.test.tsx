import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MenuItemCard } from '../components/menu/MenuItemCard';
import type { RuntimeMenuItem } from '@restaurant-order/contracts';

describe('MenuItemCard (CUST-02 / Accessibility & Pricing)', () => {
  const itemWithVariants: RuntimeMenuItem = {
    id: 'item-pizza-1',
    name: 'Karışık Pizza',
    slug: 'karisik-pizza',
    shortDescription: 'Özel hamur, zengin malzeme',
    fullDescription: 'Sucuk, sosis, mısır, zeytin, kaşar',
    imageUrl: null,
    basePriceMinorUnits: 1000, // Dummy unpurchasable base price (10 TL)
    sortOrder: 1,
    spicyLevel: 0,
    dietaryTags: [],
    allergenTags: [],
    isAvailable: true,
    expectedAvailableAtUtc: null,
    variants: [
      {
        id: 'var-small',
        name: 'Küçük Boy',
        code: 'SML',
        absolutePriceMinorUnits: 18000, // 180 TL
        sortOrder: 1,
        isDefault: false,
        isAvailable: true,
        expectedAvailableAtUtc: null,
      },
      {
        id: 'var-medium',
        name: 'Orta Boy',
        code: 'MED',
        absolutePriceMinorUnits: 25000, // 250 TL
        sortOrder: 2,
        isDefault: true,
        isAvailable: true,
        expectedAvailableAtUtc: null,
      },
      {
        id: 'var-large',
        name: 'Büyük Boy (Tükendi)',
        code: 'LRG',
        absolutePriceMinorUnits: 12000, // 120 TL but unavailable!
        sortOrder: 3,
        isDefault: false,
        isAvailable: false,
        expectedAvailableAtUtc: null,
      },
    ],
    modifierGroups: [],
  };

  const itemWithoutVariants: RuntimeMenuItem = {
    id: 'item-coke-1',
    name: 'Kola',
    slug: 'kola',
    shortDescription: 'Kutu 330ml',
    fullDescription: null,
    imageUrl: null,
    basePriceMinorUnits: 4500, // 45 TL
    sortOrder: 2,
    spicyLevel: 0,
    dietaryTags: [],
    allergenTags: [],
    isAvailable: true,
    expectedAvailableAtUtc: null,
    variants: [],
    modifierGroups: [],
  };

  it('calculates minPrice strictly from available variants without unpurchasable basePrice or unavailable variants', () => {
    render(
      <MenuItemCard
        item={itemWithVariants}
        currency="TRY"
        avoidedAllergens={[]}
        onSelect={vi.fn()}
      />
    );

    // Should display 180,00 TL (var-small) and NOT 10,00 TL (basePrice) or 120,00 TL (unavailable large)
    const priceDisplay = screen.getByTestId('item-price-item-pizza-1');
    expect(priceDisplay.textContent).toContain('180,00');
    expect(priceDisplay.textContent).not.toContain('10,00');
    expect(priceDisplay.textContent).not.toContain('120,00');

    // Shows "başlangıç fiyatı" indicator because multiple active variants exist
    expect(screen.getByText('başlangıç fiyatı')).toBeDefined();
  });

  it('uses basePriceMinorUnits when item has no variants', () => {
    render(
      <MenuItemCard
        item={itemWithoutVariants}
        currency="TRY"
        avoidedAllergens={[]}
        onSelect={vi.fn()}
      />
    );

    const priceDisplay = screen.getByTestId('item-price-item-coke-1');
    expect(priceDisplay.textContent).toContain('45,00');
    expect(screen.queryByText('başlangıç fiyatı')).toBeNull();
  });

  it('provides a single interactive role="button" control without nested buttons', () => {
    render(
      <MenuItemCard
        item={itemWithVariants}
        currency="TRY"
        avoidedAllergens={[]}
        onSelect={vi.fn()}
      />
    );

    // The card itself is the sole interactive button control
    const cardButton = screen.getByRole('button', { name: /Karışık Pizza/i });
    expect(cardButton).toBeDefined();

    // Verify there are no nested <button> elements inside the card
    const nestedButtons = cardButton.querySelectorAll('button');
    expect(nestedButtons.length).toBe(0);

    // Verify "İncele" visual indicator is aria-hidden
    const inspectIndicator = screen.getByTestId('inspect-item-btn-item-pizza-1');
    expect(inspectIndicator.getAttribute('aria-hidden')).toBe('true');
  });

  it('supports keyboard navigation via Enter and Space keys', () => {
    const handleSelect = vi.fn();

    render(
      <MenuItemCard
        item={itemWithVariants}
        currency="TRY"
        avoidedAllergens={[]}
        onSelect={handleSelect}
      />
    );

    const cardButton = screen.getByRole('button', { name: /Karışık Pizza/i });

    // Press Enter
    fireEvent.keyDown(cardButton, { key: 'Enter', code: 'Enter' });
    expect(handleSelect).toHaveBeenCalledTimes(1);
    expect(handleSelect).toHaveBeenCalledWith(itemWithVariants);

    // Press Space
    fireEvent.keyDown(cardButton, { key: ' ', code: 'Space' });
    expect(handleSelect).toHaveBeenCalledTimes(2);

    // Press other keys (e.g. Tab, Escape) should not trigger select
    fireEvent.keyDown(cardButton, { key: 'Escape', code: 'Escape' });
    expect(handleSelect).toHaveBeenCalledTimes(2);
  });
});
