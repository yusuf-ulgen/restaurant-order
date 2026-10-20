import { describe, it, expect } from 'vitest';
import {
  roundHalfUp,
  calculateLineItemFinancials,
  calculateOrderTotals,
  calculateCartFinancialSummary,
} from '@restaurant-order/contracts';

describe('OrderPricingCalculator Contract & Financial Parity', () => {
  it('RoundHalfUp_ExactBoundaryTests: exactly matches C# Half-Up boundary behavior', () => {
    expect(roundHalfUp(10001, 10000)).toBe(1);
    expect(roundHalfUp(14999, 10000)).toBe(1);
    expect(roundHalfUp(15000, 10000)).toBe(2);
    expect(roundHalfUp(15001, 10000)).toBe(2);
    expect(roundHalfUp(19999, 10000)).toBe(2);
  });

  it('CalculateLineItem_TaxIncluded_ExtractsTaxWithHalfUpRounding: matches C# vector', () => {
    // 100.00 TRY brüt, %10 KDV (1000 bps)
    const result = calculateLineItemFinancials({
      unitPriceMinorUnits: 10000,
      modifierTotalMinorUnits: 0,
      quantity: 1,
      taxRateBps: 1000,
      pricesIncludeTax: true,
    });

    expect(result.lineTotalMinorUnits).toBe(10000);
    expect(result.taxMinorUnits).toBe(909);
    expect(result.netSubtotalMinorUnits).toBe(9091);
    expect(result.netSubtotalMinorUnits + result.taxMinorUnits).toBe(result.lineTotalMinorUnits);
  });

  it('CalculateLineItem_TaxExcluded_AddsTaxOnTop: matches C# vector', () => {
    // 100.00 TRY net, %10 KDV (1000 bps)
    const result = calculateLineItemFinancials({
      unitPriceMinorUnits: 10000,
      modifierTotalMinorUnits: 0,
      quantity: 1,
      taxRateBps: 1000,
      pricesIncludeTax: false,
    });

    expect(result.lineTotalMinorUnits).toBe(10000);
    expect(result.netSubtotalMinorUnits).toBe(10000);
    expect(result.taxMinorUnits).toBe(1000);
  });

  it('CalculateLineItem_ZeroTax_TaxIsZero: matches C# vector', () => {
    const result = calculateLineItemFinancials({
      unitPriceMinorUnits: 5000,
      modifierTotalMinorUnits: 500,
      quantity: 2,
      taxRateBps: 0,
      pricesIncludeTax: true,
    });

    expect(result.lineTotalMinorUnits).toBe(11000);
    expect(result.netSubtotalMinorUnits).toBe(11000);
    expect(result.taxMinorUnits).toBe(0);
  });

  it('CalculateLineItem_WithModifiersAndQuantity: matches C# vector', () => {
    // Base: 4000, Modifiers: 1000, Quantity: 3 -> LineTotal = 15000, %20 KDV, Tax Included
    const result = calculateLineItemFinancials({
      unitPriceMinorUnits: 4000,
      modifierTotalMinorUnits: 1000,
      quantity: 3,
      taxRateBps: 2000,
      pricesIncludeTax: true,
    });

    expect(result.lineTotalMinorUnits).toBe(15000);
    expect(result.taxMinorUnits).toBe(2500);
    expect(result.netSubtotalMinorUnits).toBe(12500);
    expect(result.netSubtotalMinorUnits + result.taxMinorUnits).toBe(result.lineTotalMinorUnits);
  });

  it('CalculateOrderTotals_ServiceChargeEnabled: matches C# vector', () => {
    // Net: 10000, Tax: 1000 -> Gross: 11000, %10 Service Charge (1000 bps)
    const result = calculateOrderTotals({
      netSubtotalMinorUnits: 10000,
      taxTotalMinorUnits: 1000,
      isServiceChargeEnabled: true,
      serviceChargeRateBps: 1000,
    });

    expect(result.serviceChargeMinorUnits).toBe(1100);
    expect(result.grandTotalMinorUnits).toBe(12100);
  });

  it('CalculateOrderTotals_ServiceChargeDisabled: matches C# vector', () => {
    const result = calculateOrderTotals({
      netSubtotalMinorUnits: 10000,
      taxTotalMinorUnits: 1000,
      isServiceChargeEnabled: false,
      serviceChargeRateBps: 1000,
    });

    expect(result.serviceChargeMinorUnits).toBe(0);
    expect(result.grandTotalMinorUnits).toBe(11000);
  });

  it('CalculateCartFinancialSummary: calculates multi-line cart totals accurately', () => {
    const lines = [
      { unitPriceMinorUnits: 5000, quantity: 2 }, // 10000
      { unitPriceMinorUnits: 2500, quantity: 1 }, // 2500
    ];

    const summaryIncluded = calculateCartFinancialSummary(lines, {
      pricesIncludeTax: true,
      defaultTaxRateBps: 1000, // 10%
      isServiceChargeEnabled: true,
      serviceChargeRateBps: 500, // 5%
    });

    // Total gross = 12500
    // Tax = (12500 * 1000 + 5500) / 11000 = 12505500 / 11000 = 1136.86 -> 1137
    // Net subtotal = 12500 - 1137 = 11363
    // Service charge = (12500 * 500 + 5000) / 10000 = 6255000 / 10000 = 625.5 -> 626
    // Grand total = 12500 + 626 = 13126
    expect(summaryIncluded.netSubtotalMinorUnits).toBe(11364);
    expect(summaryIncluded.taxMinorUnits).toBe(1136);
    expect(summaryIncluded.serviceChargeMinorUnits).toBe(625);
    expect(summaryIncluded.grandTotalMinorUnits).toBe(13125);
    expect(summaryIncluded.netSubtotalMinorUnits + summaryIncluded.taxMinorUnits).toBe(12500);
    expect(summaryIncluded.pricesIncludeTax).toBe(true);

    const summaryExcluded = calculateCartFinancialSummary(lines, {
      pricesIncludeTax: false,
      defaultTaxRateBps: 1000,
      isServiceChargeEnabled: false,
      serviceChargeRateBps: 0,
    });

    // Total net = 12500
    // Tax = 1250
    // Grand total = 13750
    expect(summaryExcluded.netSubtotalMinorUnits).toBe(12500);
    expect(summaryExcluded.taxMinorUnits).toBe(1250);
    expect(summaryExcluded.serviceChargeMinorUnits).toBe(0);
    expect(summaryExcluded.grandTotalMinorUnits).toBe(13750);
  });

  it('OverflowProtection & InvalidValues: safely rejects unsafe numbers or invalid ranges', () => {
    expect(() => roundHalfUp(-5, 1000)).toThrow();
    expect(() => roundHalfUp(100, 0)).toThrow();
    expect(() => roundHalfUp(Number.MAX_SAFE_INTEGER + 1, 1000)).toThrow();

    expect(() =>
      calculateLineItemFinancials({
        unitPriceMinorUnits: 1000,
        modifierTotalMinorUnits: 0,
        quantity: 0, // invalid quantity
        taxRateBps: 1000,
        pricesIncludeTax: true,
      })
    ).toThrow();

    expect(() =>
      calculateLineItemFinancials({
        unitPriceMinorUnits: 1000,
        modifierTotalMinorUnits: 0,
        quantity: 100, // invalid quantity > 99
        taxRateBps: 1000,
        pricesIncludeTax: true,
      })
    ).toThrow();
  });
});
