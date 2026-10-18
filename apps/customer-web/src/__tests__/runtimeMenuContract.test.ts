import { describe, it, expect } from 'vitest';
import type {
  RuntimeMenuReadModel,
  RuntimeMenu,
  RuntimeMenuCategory,
  RuntimeMenuItem,
  RuntimeItemVariant,
  RuntimeModifierGroup,
  RuntimeModifierOption,
} from '@restaurant-order/contracts';

describe('Runtime Menu Contracts & Drift Validation', () => {
  it('correctly maps and types full backend RuntimeMenuReadModel JSON', () => {
    const rawPayload: RuntimeMenuReadModel = {
      branchId: 'b1111111-1111-1111-1111-111111111111',
      currency: 'TRY',
      pricesIncludeTax: true,
      defaultTaxRateBps: 1000,
      isServiceChargeEnabled: true,
      serviceChargeRateBps: 500,
      menus: [
        {
          id: 'm2222222-2222-2222-2222-222222222222',
          name: 'Ana Menü',
          slug: 'ana-menu',
          description: 'Günlük taze lezzetler',
          sortOrder: 1,
          categories: [
            {
              id: 'c3333333-3333-3333-3333-333333333333',
              name: 'Burgerler',
              slug: 'burgerler',
              description: 'El yapımı burgerler',
              sortOrder: 1,
              items: [
                {
                  id: 'i4444444-4444-4444-4444-444444444444',
                  name: 'Truffle Burger',
                  slug: 'truffle-burger',
                  shortDescription: 'Trüf mayonezli burger',
                  fullDescription: '200g dana köfte, trüflü mayonez, karamelize mantar, brioche ekmeği',
                  imageUrl: 'https://cdn.example.com/images/truffle.jpg',
                  basePriceMinorUnits: 32000,
                  sortOrder: 1,
                  spicyLevel: 0,
                  dietaryTags: ['Halal'],
                  allergenTags: ['Gluten', 'Eggs', 'Lactose'],
                  isAvailable: true,
                  expectedAvailableAtUtc: null,
                  variants: [
                    {
                      id: 'v5555555-5555-5555-5555-555555555555',
                      name: 'Tek Köfte',
                      code: 'SINGLE',
                      absolutePriceMinorUnits: 32000,
                      sortOrder: 1,
                      isDefault: true,
                      isAvailable: true,
                      expectedAvailableAtUtc: null,
                    },
                    {
                      id: 'v5555556-5555-5555-5555-555555555556',
                      name: 'Duble Köfte',
                      code: 'DOUBLE',
                      absolutePriceMinorUnits: 42000,
                      sortOrder: 2,
                      isDefault: false,
                      isAvailable: false,
                      expectedAvailableAtUtc: '2026-10-16T22:00:00Z',
                    },
                  ],
                  modifierGroups: [
                    {
                      id: 'g6666666-6666-6666-6666-666666666666',
                      name: 'Ekstra Sos',
                      minSelections: 0,
                      maxSelections: 2,
                      sortOrder: 1,
                      options: [
                        {
                          id: 'o7777777-7777-7777-7777-777777777777',
                          name: 'Trüf Sos',
                          priceDeltaMinorUnits: 2500,
                          sortOrder: 1,
                          isDefault: false,
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

    expect(rawPayload.branchId).toBe('b1111111-1111-1111-1111-111111111111');
    expect(rawPayload.currency).toBe('TRY');
    expect(rawPayload.pricesIncludeTax).toBe(true);
    expect(rawPayload.defaultTaxRateBps).toBe(1000);
    expect(rawPayload.isServiceChargeEnabled).toBe(true);
    expect(rawPayload.serviceChargeRateBps).toBe(500);
    expect(rawPayload.menus.length).toBe(1);

    const menu: RuntimeMenu = rawPayload.menus[0]!;
    expect(menu.name).toBe('Ana Menü');

    const category: RuntimeMenuCategory = menu.categories[0]!;
    expect(category.name).toBe('Burgerler');

    const item: RuntimeMenuItem = category.items[0]!;
    expect(item.name).toBe('Truffle Burger');
    expect(item.basePriceMinorUnits).toBe(32000);
    expect(item.dietaryTags).toContain('Halal');
    expect(item.allergenTags).toContain('Gluten');
    expect(item.isAvailable).toBe(true);

    const variant: RuntimeItemVariant = item.variants[1]!;
    expect(variant.name).toBe('Duble Köfte');
    expect(variant.isAvailable).toBe(false);
    expect(variant.expectedAvailableAtUtc).toBe('2026-10-16T22:00:00Z');

    const modifierGroup: RuntimeModifierGroup = item.modifierGroups[0]!;
    expect(modifierGroup.name).toBe('Ekstra Sos');

    const option: RuntimeModifierOption = modifierGroup.options[0]!;
    expect(option.priceDeltaMinorUnits).toBe(2500);
  });
});
