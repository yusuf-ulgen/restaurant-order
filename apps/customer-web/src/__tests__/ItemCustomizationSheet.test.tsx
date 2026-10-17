import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import type { RuntimeMenuItem, DraftCartLine } from '@restaurant-order/contracts';
import { ItemCustomizationSheet } from '../components/customization/ItemCustomizationSheet';

describe('ItemCustomizationSheet Component Tests', () => {
  const sampleItemWithVariants: RuntimeMenuItem = {
    id: 'item-burger-1',
    name: 'Gurme Burger',
    slug: 'gurme-burger',
    shortDescription: 'Özel sos ve karamelize soğan',
    fullDescription: '150g dinlendirilmiş dana eti, brioche ekmeği ve cheddar peyniri.',
    imageUrl: 'https://example.com/burger.jpg',
    basePriceMinorUnits: 18000,
    sortOrder: 1,
    spicyLevel: 2,
    dietaryTags: ['Popüler'],
    allergenTags: ['Gluten', 'Süt'],
    isAvailable: true,
    expectedAvailableAtUtc: null,
    variants: [
      {
        id: 'var-150g',
        name: '150g Tek Köfte',
        code: '150G',
        absolutePriceMinorUnits: 18000,
        sortOrder: 1,
        isDefault: true,
        isAvailable: true,
        expectedAvailableAtUtc: null,
      },
      {
        id: 'var-300g',
        name: '300g Çift Köfte',
        code: '300G',
        absolutePriceMinorUnits: 27000,
        sortOrder: 2,
        isDefault: false,
        isAvailable: true,
        expectedAvailableAtUtc: null,
      },
      {
        id: 'var-450g',
        name: '450g Dev Köfte',
        code: '450G',
        absolutePriceMinorUnits: 36000,
        sortOrder: 3,
        isDefault: false,
        isAvailable: false,
        expectedAvailableAtUtc: '2026-10-17T12:00:00Z',
      },
    ],
    modifierGroups: [
      {
        id: 'group-bread',
        name: 'Ekmek Tercihi',
        minSelections: 1,
        maxSelections: 1,
        sortOrder: 1,
        options: [
          {
            id: 'opt-brioche',
            name: 'Brioche Ekmek',
            priceDeltaMinorUnits: 0,
            sortOrder: 1,
            isDefault: true,
          },
          {
            id: 'opt-glutenfree',
            name: 'Glutensiz Ekmek',
            priceDeltaMinorUnits: 2000,
            sortOrder: 2,
            isDefault: false,
          },
        ],
      },
      {
        id: 'group-extras',
        name: 'Ekstra Malzemeler',
        minSelections: 0,
        maxSelections: 2,
        sortOrder: 2,
        options: [
          {
            id: 'opt-cheddar',
            name: 'Ekstra Cheddar',
            priceDeltaMinorUnits: 2500,
            sortOrder: 1,
            isDefault: false,
          },
          {
            id: 'opt-caramelized',
            name: 'Ekstra Karamelize Soğan',
            priceDeltaMinorUnits: 1500,
            sortOrder: 2,
            isDefault: false,
          },
          {
            id: 'opt-bacon',
            name: 'Dana Füme',
            priceDeltaMinorUnits: 4000,
            sortOrder: 3,
            isDefault: false,
          },
        ],
      },
    ],
  };

  const itemWithoutVariants: RuntimeMenuItem = {
    id: 'item-drink-1',
    name: 'Ev Yapımı Limonata',
    slug: 'ev-yapimi-limonata',
    shortDescription: 'Taze nane ve limon',
    fullDescription: 'Günlük sıkılan limonlarla hazırlanan ferahlatıcı limonata.',
    imageUrl: 'https://example.com/lemonade.jpg',
    basePriceMinorUnits: 4500,
    sortOrder: 2,
    spicyLevel: 0,
    dietaryTags: ['Vegan'],
    allergenTags: [],
    isAvailable: true,
    expectedAvailableAtUtc: null,
    variants: [],
    modifierGroups: [],
  };

  const itemWithOutOfStockDefault: RuntimeMenuItem = {
    ...sampleItemWithVariants,
    id: 'item-oos-default',
    variants: [
      {
        id: 'var-default-oos',
        name: 'Normal Porsiyon',
        code: 'NRM',
        absolutePriceMinorUnits: 15000,
        sortOrder: 1,
        isDefault: true,
        isAvailable: false,
        expectedAvailableAtUtc: null,
      },
      {
        id: 'var-alt-available',
        name: 'Büyük Porsiyon',
        code: 'BYK',
        absolutePriceMinorUnits: 22000,
        sortOrder: 2,
        isDefault: false,
        isAvailable: true,
        expectedAvailableAtUtc: null,
      },
    ],
  };

  it('renders item details with image, description, and allergen warning', () => {
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
        avoidedAllergens={['Gluten']}
      />
    );

    expect(screen.getAllByText('Gurme Burger').length).toBeGreaterThan(0);
    expect(screen.getByText(/150g dinlendirilmiş dana eti/i)).toBeDefined();
    expect(screen.getByText('⚠️ Gluten içerir')).toBeDefined();
  });

  it('handles item without variants and calculates price directly from base price', () => {
    const handleAddToCart = vi.fn();
    render(
      <ItemCustomizationSheet
        item={itemWithoutVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
        onAddToCart={handleAddToCart}
      />
    );

    expect(screen.getByTestId('unit-price-display').textContent).toContain('45,00');
    expect(screen.getByTestId('line-total-display').textContent).toContain('45,00');

    const ctaBtn = screen.getByTestId('add-to-cart-cta-btn') as HTMLButtonElement;
    expect(ctaBtn.disabled).toBe(false);

    fireEvent.click(ctaBtn);

    expect(handleAddToCart).toHaveBeenCalledTimes(1);
    const addedLine = handleAddToCart.mock.calls[0]![0] as DraftCartLine;
    expect(addedLine.menuItemId).toBe('item-drink-1');
    expect(addedLine.variantId).toBeNull();
    expect(addedLine.unitPriceMinorUnits).toBe(4500);
    expect(addedLine.lineTotalMinorUnits).toBe(4500);
    expect(addedLine.quantity).toBe(1);
  });

  it('auto-selects available default variant on open', () => {
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    const singleVariantRadio = screen.getByLabelText(/150g Tek Köfte/i) as HTMLInputElement;
    expect(singleVariantRadio.checked).toBe(true);
    expect(screen.getByTestId('unit-price-display').textContent).toContain('180,00');
  });

  it('does NOT auto-select non-default variant when default variant is out of stock', () => {
    render(
      <ItemCustomizationSheet
        item={itemWithOutOfStockDefault}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    const defaultRadio = screen.getByLabelText(/Normal Porsiyon.*Tükendi/i) as HTMLInputElement;
    expect(defaultRadio.disabled).toBe(true);
    expect(defaultRadio.checked).toBe(false);

    const altRadio = screen.getByLabelText(/Büyük Porsiyon/i) as HTMLInputElement;
    expect(altRadio.checked).toBe(false);

    const ctaBtn = screen.getByTestId('add-to-cart-cta-btn') as HTMLButtonElement;
    expect(ctaBtn.disabled).toBe(true);
  });

  it('disables out-of-stock variant and prevents selecting it', () => {
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    const oosRadio = screen.getByLabelText(/450g Dev Köfte.*Tükendi/i) as HTMLInputElement;
    expect(oosRadio.disabled).toBe(true);
  });

  it('switches variant and updates unit price and line total dynamically', () => {
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    const doubleRadio = screen.getByLabelText(/300g Çift Köfte/i) as HTMLInputElement;
    fireEvent.click(doubleRadio);

    expect(doubleRadio.checked).toBe(true);
    expect(screen.getByTestId('unit-price-display').textContent).toContain('270,00');
    expect(screen.getByTestId('line-total-display').textContent).toContain('270,00');
  });

  it('applies modifier price deltas correctly', () => {
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    expect(screen.getByTestId('unit-price-display').textContent).toContain('180,00');

    const gfBread = screen.getByLabelText(/Glutensiz Ekmek/i);
    fireEvent.click(gfBread);

    expect(screen.getByTestId('unit-price-display').textContent).toContain('200,00');

    const cheddar = screen.getByLabelText(/Ekstra Cheddar/i);
    fireEvent.click(cheddar);

    expect(screen.getByTestId('unit-price-display').textContent).toContain('225,00');
  });

  it('enforces modifier maxSelections bound and shows explicit error without silent deselection', () => {
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    const cheddar = screen.getByLabelText(/Ekstra Cheddar/i) as HTMLInputElement;
    const onion = screen.getByLabelText(/Ekstra Karamelize Soğan/i) as HTMLInputElement;
    const bacon = screen.getByLabelText(/Dana Füme/i) as HTMLInputElement;

    fireEvent.click(cheddar);
    fireEvent.click(onion);

    expect(cheddar.checked).toBe(true);
    expect(onion.checked).toBe(true);

    fireEvent.click(bacon);

    const errorMsg = screen.getByTestId('modifier-error-group-extras');
    expect(errorMsg.textContent).toContain('en fazla 2 seçim');

    expect(cheddar.checked).toBe(true);
    expect(onion.checked).toBe(true);
    expect(bacon.checked).toBe(false);
  });

  it('increments and decrements quantity and bounds it to min 1 and max 99', () => {
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    const decrementBtn = screen.getByTestId('quantity-decrement-btn') as HTMLButtonElement;
    const incrementBtn = screen.getByTestId('quantity-increment-btn') as HTMLButtonElement;
    const quantityDisplay = screen.getByTestId('quantity-display');

    expect(quantityDisplay.textContent).toBe('1');
    expect(decrementBtn.disabled).toBe(true);

    fireEvent.click(incrementBtn);
    fireEvent.click(incrementBtn);

    expect(quantityDisplay.textContent).toBe('3');
    expect(screen.getByTestId('line-total-display').textContent).toContain('540,00');

    fireEvent.click(decrementBtn);
    expect(quantityDisplay.textContent).toBe('2');
    expect(screen.getByTestId('line-total-display').textContent).toContain('360,00');
  });

  it('normalizes customer note and includes it in draft cart line', () => {
    const handleAddToCart = vi.fn();
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
        onAddToCart={handleAddToCart}
      />
    );

    const noteInput = screen.getByTestId('customer-note-input');
    fireEvent.change(noteInput, { target: { value: '  Soğanı bol olsun lütfen   ' } });

    expect(screen.getByTestId('customer-note-counter').textContent).toContain('28 / 200');

    const ctaBtn = screen.getByTestId('add-to-cart-cta-btn');
    fireEvent.click(ctaBtn);

    expect(handleAddToCart).toHaveBeenCalledTimes(1);
    const addedLine = handleAddToCart.mock.calls[0]![0] as DraftCartLine;
    expect(addedLine.customerNote).toBe('Soğanı bol olsun lütfen');
    expect(addedLine.configurationSignature).toContain('Soğanı bol olsun lütfen');
  });

  it('disables CTA and displays warning if overall item is marked out of stock', () => {
    const outOfStockItem: RuntimeMenuItem = {
      ...sampleItemWithVariants,
      isAvailable: false,
    };

    render(
      <ItemCustomizationSheet
        item={outOfStockItem}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    expect(screen.getByTestId('item-out-of-stock-alert')).toBeDefined();
    const ctaBtn = screen.getByTestId('add-to-cart-cta-btn') as HTMLButtonElement;
    expect(ctaBtn.disabled).toBe(true);
  });

  it('resets state when reopened or when item changes', () => {
    const { rerender } = render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    fireEvent.click(screen.getByTestId('quantity-increment-btn'));
    expect(screen.getByTestId('quantity-display').textContent).toBe('2');

    rerender(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={false}
        onClose={vi.fn()}
      />
    );

    rerender(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={vi.fn()}
      />
    );

    expect(screen.getByTestId('quantity-display').textContent).toBe('1');
    const noteInput = screen.getByTestId('customer-note-input') as HTMLTextAreaElement;
    expect(noteInput.value).toBe('');
  });

  it('calls onClose on Escape key press and backdrop click', () => {
    const handleClose = vi.fn();
    render(
      <ItemCustomizationSheet
        item={sampleItemWithVariants}
        currency="TRY"
        isOpen={true}
        onClose={handleClose}
      />
    );

    fireEvent.keyDown(document, { key: 'Escape', code: 'Escape' });
    expect(handleClose).toHaveBeenCalled();
  });
});
