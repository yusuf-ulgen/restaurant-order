import { describe, it, expect } from 'vitest';
import type { RuntimeMenuItem } from '@restaurant-order/contracts';
import {
  normalizeCustomerNote,
  generateConfigurationSignature,
  calculateItemPrice,
  validateCustomization,
} from '../utils/customizationUtils';

describe('customizationUtils Unit Tests', () => {
  const baseItem: RuntimeMenuItem = {
    id: 'item-1',
    name: 'Klasik Burger',
    slug: 'klasik-burger',
    shortDescription: 'Nefis köfte ve taze sebzeler',
    fullDescription: 'Özenle seçilmiş dana etiyle hazırlanan enfes burger.',
    imageUrl: 'https://example.com/burger.jpg',
    basePriceMinorUnits: 15000,
    sortOrder: 1,
    spicyLevel: 1,
    dietaryTags: [],
    allergenTags: ['Gluten', 'Süt'],
    isAvailable: true,
    expectedAvailableAtUtc: null,
    variants: [
      {
        id: 'var-single',
        name: 'Tek Köfteli',
        code: 'SNG',
        absolutePriceMinorUnits: 15000,
        sortOrder: 1,
        isDefault: true,
        isAvailable: true,
        expectedAvailableAtUtc: null,
      },
      {
        id: 'var-double',
        name: 'Çift Köfteli',
        code: 'DBL',
        absolutePriceMinorUnits: 22000,
        sortOrder: 2,
        isDefault: false,
        isAvailable: true,
        expectedAvailableAtUtc: null,
      },
      {
        id: 'var-out-of-stock',
        name: 'Triple Mega',
        code: 'TRP',
        absolutePriceMinorUnits: 30000,
        sortOrder: 3,
        isDefault: false,
        isAvailable: false,
        expectedAvailableAtUtc: null,
      },
    ],
    modifierGroups: [
      {
        id: 'group-sauce',
        name: 'Sos Seçimi',
        minSelections: 1,
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
          {
            id: 'opt-bbq',
            name: 'Barbekü Sos',
            priceDeltaMinorUnits: 1500,
            sortOrder: 2,
            isDefault: false,
          },
        ],
      },
      {
        id: 'group-extra',
        name: 'Ekstra Malzeme',
        minSelections: 0,
        maxSelections: 3,
        sortOrder: 2,
        options: [
          {
            id: 'opt-cheese',
            name: 'Ekstra Peynir',
            priceDeltaMinorUnits: 2500,
            sortOrder: 1,
            isDefault: false,
          },
          {
            id: 'opt-bacon',
            name: 'Dana Bacon',
            priceDeltaMinorUnits: 4000,
            sortOrder: 2,
            isDefault: false,
          },
        ],
      },
    ],
  };

  const simpleItemWithoutVariants: RuntimeMenuItem = {
    ...baseItem,
    id: 'item-fries',
    name: 'Patates Kızartması',
    basePriceMinorUnits: 6500,
    variants: [],
    modifierGroups: [],
  };

  describe('normalizeCustomerNote', () => {
    it('returns null for null, undefined, or empty string', () => {
      expect(normalizeCustomerNote(null)).toBeNull();
      expect(normalizeCustomerNote(undefined)).toBeNull();
      expect(normalizeCustomerNote('')).toBeNull();
    });

    it('returns null for whitespace-only strings', () => {
      expect(normalizeCustomerNote('   \t\n  ')).toBeNull();
    });

    it('trims leading and trailing whitespace', () => {
      expect(normalizeCustomerNote('  Soğansız olsun lütfen   ')).toBe('Soğansız olsun lütfen');
    });

    it('strips control characters', () => {
      const input = 'Ekstra\x00\x08 sos\x1F lütfen';
      expect(normalizeCustomerNote(input)).toBe('Ekstra sos lütfen');
    });

    it('truncates note if exceeding CUSTOMER_NOTE_MAX_LENGTH (200)', () => {
      const longNote = 'A'.repeat(250);
      const normalized = normalizeCustomerNote(longNote);
      expect(normalized).toHaveLength(200);
      expect(normalized).toBe('A'.repeat(200));
    });
  });

  describe('generateConfigurationSignature', () => {
    it('produces the same signature regardless of modifier option selection order', () => {
      const sig1 = generateConfigurationSignature({
        menuItemId: 'item-1',
        variantId: 'var-double',
        selectedModifierOptionIds: ['opt-bbq', 'opt-cheese'],
        customerNote: 'Az tuzlu',
      });

      const sig2 = generateConfigurationSignature({
        menuItemId: 'item-1',
        variantId: 'var-double',
        selectedModifierOptionIds: ['opt-cheese', 'opt-bbq'],
        customerNote: 'Az tuzlu',
      });

      expect(sig1).toBe(sig2);
    });

    it('produces distinct signatures for different variants', () => {
      const sig1 = generateConfigurationSignature({
        menuItemId: 'item-1',
        variantId: 'var-single',
        selectedModifierOptionIds: ['opt-mayo'],
        customerNote: null,
      });

      const sig2 = generateConfigurationSignature({
        menuItemId: 'item-1',
        variantId: 'var-double',
        selectedModifierOptionIds: ['opt-mayo'],
        customerNote: null,
      });

      expect(sig1).not.toBe(sig2);
    });

    it('produces distinct signatures for different notes', () => {
      const sig1 = generateConfigurationSignature({
        menuItemId: 'item-1',
        variantId: 'var-single',
        selectedModifierOptionIds: [],
        customerNote: 'Acı olmasın',
      });

      const sig2 = generateConfigurationSignature({
        menuItemId: 'item-1',
        variantId: 'var-single',
        selectedModifierOptionIds: [],
        customerNote: 'Bol acılı olsun',
      });

      expect(sig1).not.toBe(sig2);
    });
  });

  describe('calculateItemPrice', () => {
    it('calculates price for item without variants using basePriceMinorUnits', () => {
      const result = calculateItemPrice({
        item: simpleItemWithoutVariants,
        selectedVariantId: null,
        selectedModifierOptionIds: [],
        quantity: 2,
      });

      expect(result.unitPriceMinorUnits).toBe(6500);
      expect(result.lineTotalMinorUnits).toBe(13000);
    });

    it('calculates price using selected variant absolutePriceMinorUnits', () => {
      const result = calculateItemPrice({
        item: baseItem,
        selectedVariantId: 'var-double',
        selectedModifierOptionIds: [],
        quantity: 1,
      });

      expect(result.unitPriceMinorUnits).toBe(22000);
      expect(result.lineTotalMinorUnits).toBe(22000);
    });

    it('adds modifier price deltas correctly to variant price', () => {
      const result = calculateItemPrice({
        item: baseItem,
        selectedVariantId: 'var-double', // 22000
        selectedModifierOptionIds: ['opt-bbq', 'opt-cheese'], // +1500 + 2500 = +4000
        quantity: 3,
      });

      expect(result.unitPriceMinorUnits).toBe(26000);
      expect(result.lineTotalMinorUnits).toBe(78000);
    });

    it('allows negative price deltas if total unit price remains non-negative', () => {
      const itemWithDiscountModifier: RuntimeMenuItem = {
        ...simpleItemWithoutVariants,
        modifierGroups: [
          {
            id: 'discount-group',
            name: 'Porsiyon İndirimi',
            minSelections: 0,
            maxSelections: 1,
            sortOrder: 1,
            options: [
              {
                id: 'opt-discount',
                name: 'Öğrenci İndirimi',
                priceDeltaMinorUnits: -1500,
                sortOrder: 1,
                isDefault: false,
              },
            ],
          },
        ],
      };

      const result = calculateItemPrice({
        item: itemWithDiscountModifier,
        selectedVariantId: null,
        selectedModifierOptionIds: ['opt-discount'],
        quantity: 1,
      });

      expect(result.unitPriceMinorUnits).toBe(5000);
      expect(result.lineTotalMinorUnits).toBe(5000);
    });

    it('throws error if net unit price becomes negative', () => {
      const itemWithBigDiscount: RuntimeMenuItem = {
        ...simpleItemWithoutVariants,
        modifierGroups: [
          {
            id: 'discount-group',
            name: 'Aşırı İndirim',
            minSelections: 0,
            maxSelections: 1,
            sortOrder: 1,
            options: [
              {
                id: 'opt-big-discount',
                name: 'Bedava Kupon',
                priceDeltaMinorUnits: -10000,
                sortOrder: 1,
                isDefault: false,
              },
            ],
          },
        ],
      };

      expect(() =>
        calculateItemPrice({
          item: itemWithBigDiscount,
          selectedVariantId: null,
          selectedModifierOptionIds: ['opt-big-discount'],
          quantity: 1,
        })
      ).toThrow('Birim fiyat negatif olamaz.');
    });

    it('rejects invalid or fractional quantity', () => {
      expect(() =>
        calculateItemPrice({
          item: simpleItemWithoutVariants,
          selectedVariantId: null,
          selectedModifierOptionIds: [],
          quantity: 1.5,
        })
      ).toThrow('Geçersiz miktar');

      expect(() =>
        calculateItemPrice({
          item: simpleItemWithoutVariants,
          selectedVariantId: null,
          selectedModifierOptionIds: [],
          quantity: 0,
        })
      ).toThrow('Geçersiz miktar');
    });

    it('rejects unknown variant ID', () => {
      expect(() =>
        calculateItemPrice({
          item: baseItem,
          selectedVariantId: 'var-nonexistent',
          selectedModifierOptionIds: [],
          quantity: 1,
        })
      ).toThrow('Seçilen varyant bulunamadı');
    });

    it('rejects unknown modifier option ID', () => {
      expect(() =>
        calculateItemPrice({
          item: baseItem,
          selectedVariantId: 'var-single',
          selectedModifierOptionIds: ['opt-unknown'],
          quantity: 1,
        })
      ).toThrow('Seçilen özelleştirme seçeneği bulunamadı');
    });
  });

  describe('validateCustomization', () => {
    it('returns error when item has variants and no variant is selected', () => {
      const validation = validateCustomization({
        item: baseItem,
        selectedVariantId: null,
        selectedModifiers: {
          'group-sauce': ['opt-mayo'],
        },
      });

      expect(validation.isValid).toBe(false);
      expect(validation.errors.variant).toBe('Lütfen bir porsiyon/seçenek belirleyin.');
    });

    it('returns error when selected variant is out of stock', () => {
      const validation = validateCustomization({
        item: baseItem,
        selectedVariantId: 'var-out-of-stock',
        selectedModifiers: {
          'group-sauce': ['opt-mayo'],
        },
      });

      expect(validation.isValid).toBe(false);
      expect(validation.errors.variant).toContain('stokta bulunmamaktadır');
    });

    it('returns error when modifier group minSelections requirement is not met', () => {
      const validation = validateCustomization({
        item: baseItem,
        selectedVariantId: 'var-single',
        selectedModifiers: {
          'group-sauce': [], // min is 1
        },
      });

      expect(validation.isValid).toBe(false);
      expect(validation.errors['group-sauce']).toBe('Bu gruptan en az 1 seçim yapmalısınız.');
    });

    it('returns error when modifier group maxSelections bound is exceeded', () => {
      const validation = validateCustomization({
        item: baseItem,
        selectedVariantId: 'var-single',
        selectedModifiers: {
          'group-sauce': ['opt-mayo', 'opt-bbq', 'opt-extra'], // max is 2
        },
      });

      expect(validation.isValid).toBe(false);
      expect(validation.errors['group-sauce']).toBe('Bu gruptan en fazla 2 seçim yapabilirsiniz.');
    });

    it('returns isValid true when all requirements are satisfied', () => {
      const validation = validateCustomization({
        item: baseItem,
        selectedVariantId: 'var-single',
        selectedModifiers: {
          'group-sauce': ['opt-mayo'],
          'group-extra': ['opt-cheese'],
        },
      });

      expect(validation.isValid).toBe(true);
      expect(Object.keys(validation.errors)).toHaveLength(0);
    });
  });
});
