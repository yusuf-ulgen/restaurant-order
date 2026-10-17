import { describe, it, expect } from 'vitest';
import type {
  CustomerOrderResponseDto,
  CreateCustomerOrderRequest,
} from '@restaurant-order/contracts';

describe('Customer Order Contracts & Drift Validation', () => {
  it('correctly maps and types full backend CustomerOrderResponseDto JSON', () => {
    const rawPayload: CustomerOrderResponseDto = {
      id: '11111111-1111-1111-1111-111111111111',
      tenantId: '22222222-2222-2222-2222-222222222222',
      branchId: '33333333-3333-3333-3333-333333333333',
      diningSessionId: '44444444-4444-4444-4444-444444444444',
      tableId: '55555555-5555-5555-5555-555555555555',
      roundNumber: 1,
      orderSource: 'Customer',
      status: 'Submitted',
      currency: 'TRY',
      subtotalMinorUnits: 5000,
      taxMinorUnits: 500,
      serviceChargeMinorUnits: 250,
      totalMinorUnits: 5750,
      idempotencyKey: 'ord-test-key-123',
      concurrencyToken: '66666666-6666-6666-6666-666666666666',
      createdAtUtc: '2026-10-17T12:00:00Z',
      submittedAtUtc: '2026-10-17T12:00:00Z',
      items: [
        {
          id: '77777777-7777-7777-7777-777777777777',
          menuItemId: '88888888-8888-8888-8888-888888888888',
          itemVariantId: '99999999-9999-9999-9999-999999999999',
          itemName: 'Klasik Burger',
          variantName: 'Duble',
          quantity: 2,
          customerNote: 'Acısız lütfen',
          unitPriceMinorUnits: 2500,
          modifierTotalMinorUnits: 300,
          lineTotalMinorUnits: 5600,
          taxRateBasisPoints: 1000,
          status: 'Submitted',
          modifiers: [
            {
              id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
              modifierGroupId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
              modifierGroupName: 'Peynir Seçimi',
              modifierOptionId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
              modifierOptionName: 'Ekstra Cheddar',
              priceDeltaMinorUnits: 300,
            },
          ],
        },
      ],
    };

    expect(rawPayload.id).toBe('11111111-1111-1111-1111-111111111111');
    expect(rawPayload.roundNumber).toBe(1);
    expect(rawPayload.status).toBe('Submitted');
    expect(rawPayload.totalMinorUnits).toBe(5750);
    expect(rawPayload.items).toHaveLength(1);

    const item = rawPayload.items[0]!;
    expect(item.itemName).toBe('Klasik Burger');
    expect(item.quantity).toBe(2);
    expect(item.modifiers).toHaveLength(1);
    expect(item.modifiers[0]!.modifierOptionName).toBe('Ekstra Cheddar');
  });

  it('correctly forms CreateCustomerOrderRequest matching backend expectations', () => {
    const request: CreateCustomerOrderRequest = {
      items: [
        {
          menuItemId: '88888888-8888-8888-8888-888888888888',
          itemVariantId: null,
          quantity: 1,
          customerNote: 'Ketçap istemiyorum',
          selectedModifiers: [
            {
              modifierGroupId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
              modifierOptionId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
            },
          ],
          clientExpectedUnitPriceMinorUnits: 2500,
        },
      ],
    };

    expect(request.items).toHaveLength(1);
    expect(request.items[0]!.clientExpectedUnitPriceMinorUnits).toBe(2500);
    expect(request.items[0]!.selectedModifiers).toHaveLength(1);
  });
});
