/**
 * Authoritative pure integer/minor-unit financial calculator for restaurant orders.
 * Implements deterministic Half-Up arithmetic, matching C# OrderPricingCalculator exactly.
 */

export const BASIS_POINTS_DIVISOR = 10000;

/**
 * Standard integer Half-Up (away from zero) division:
 * (numerator + denominator / 2) / denominator
 */
export function roundHalfUp(numerator: number, denominator: number): number {
  if (!Number.isSafeInteger(numerator) || !Number.isSafeInteger(denominator)) {
    throw new Error('Numerator and denominator must be safe integers.');
  }

  if (denominator <= 0) {
    throw new Error(`Denominator must be greater than zero. Received: ${denominator}`);
  }

  if (numerator < 0) {
    throw new Error(`Numerator cannot be negative in financial calculations. Received: ${numerator}`);
  }

  const halfDenominator = Math.floor(denominator / 2);
  const result = Math.floor((numerator + halfDenominator) / denominator);

  if (!Number.isSafeInteger(result)) {
    throw new Error('Calculation resulted in unsafe integer overflow.');
  }

  return result;
}

export interface LineItemFinancials {
  lineTotalMinorUnits: number;
  netSubtotalMinorUnits: number;
  taxMinorUnits: number;
}

/**
 * Calculates individual line item financials based on effective unit price, modifiers, quantity, and tax policy.
 */
export function calculateLineItemFinancials(params: {
  unitPriceMinorUnits: number;
  modifierTotalMinorUnits: number;
  quantity: number;
  taxRateBps: number;
  pricesIncludeTax: boolean;
}): LineItemFinancials {
  const {
    unitPriceMinorUnits,
    modifierTotalMinorUnits,
    quantity,
    taxRateBps,
    pricesIncludeTax,
  } = params;

  if (!Number.isSafeInteger(unitPriceMinorUnits) || unitPriceMinorUnits < 0) {
    throw new Error('Unit price must be a non-negative safe integer.');
  }

  if (!Number.isSafeInteger(modifierTotalMinorUnits) || modifierTotalMinorUnits < 0) {
    throw new Error('Modifier total must be a non-negative safe integer.');
  }

  if (!Number.isSafeInteger(quantity) || quantity < 1 || quantity > 99) {
    throw new Error(`Quantity must be an integer between 1 and 99. Received: ${quantity}.`);
  }

  if (!Number.isSafeInteger(taxRateBps) || taxRateBps < 0 || taxRateBps > BASIS_POINTS_DIVISOR) {
    throw new Error(`Tax rate basis points must be between 0 and ${BASIS_POINTS_DIVISOR}. Received: ${taxRateBps}.`);
  }

  const singleItemTotal = unitPriceMinorUnits + modifierTotalMinorUnits;
  if (!Number.isSafeInteger(singleItemTotal)) {
    throw new Error('Single item total resulted in unsafe integer overflow.');
  }

  const lineTotal = singleItemTotal * quantity;
  if (!Number.isSafeInteger(lineTotal)) {
    throw new Error('Line total resulted in unsafe integer overflow.');
  }

  if (taxRateBps === 0) {
    return {
      lineTotalMinorUnits: lineTotal,
      netSubtotalMinorUnits: lineTotal,
      taxMinorUnits: 0,
    };
  }

  if (pricesIncludeTax) {
    // Catalog price includes tax (gross)
    // Tax = (lineTotal * taxRateBps) / (10000 + taxRateBps)
    const numerator = lineTotal * taxRateBps;
    const denominator = BASIS_POINTS_DIVISOR + taxRateBps;
    if (!Number.isSafeInteger(numerator)) {
      throw new Error('Tax numerator calculation resulted in unsafe integer overflow.');
    }

    const tax = roundHalfUp(numerator, denominator);
    const netSubtotal = lineTotal - tax;

    return {
      lineTotalMinorUnits: lineTotal,
      netSubtotalMinorUnits: netSubtotal,
      taxMinorUnits: tax,
    };
  } else {
    // Catalog price excludes tax (net)
    // Tax = (lineTotal * taxRateBps) / 10000
    const numerator = lineTotal * taxRateBps;
    if (!Number.isSafeInteger(numerator)) {
      throw new Error('Tax numerator calculation resulted in unsafe integer overflow.');
    }

    const tax = roundHalfUp(numerator, BASIS_POINTS_DIVISOR);

    return {
      lineTotalMinorUnits: lineTotal,
      netSubtotalMinorUnits: lineTotal,
      taxMinorUnits: tax,
    };
  }
}

export interface OrderTotalsFinancials {
  serviceChargeMinorUnits: number;
  grandTotalMinorUnits: number;
}

/**
 * Calculates order totals (service charge and grand total) given net subtotal and tax total.
 */
export function calculateOrderTotals(params: {
  netSubtotalMinorUnits: number;
  taxTotalMinorUnits: number;
  isServiceChargeEnabled: boolean;
  serviceChargeRateBps: number;
}): OrderTotalsFinancials {
  const {
    netSubtotalMinorUnits,
    taxTotalMinorUnits,
    isServiceChargeEnabled,
    serviceChargeRateBps,
  } = params;

  if (!Number.isSafeInteger(netSubtotalMinorUnits) || netSubtotalMinorUnits < 0) {
    throw new Error('Net subtotal must be a non-negative safe integer.');
  }

  if (!Number.isSafeInteger(taxTotalMinorUnits) || taxTotalMinorUnits < 0) {
    throw new Error('Tax total must be a non-negative safe integer.');
  }

  if (!Number.isSafeInteger(serviceChargeRateBps) || serviceChargeRateBps < 0 || serviceChargeRateBps > BASIS_POINTS_DIVISOR) {
    throw new Error(`Service charge rate basis points must be between 0 and ${BASIS_POINTS_DIVISOR}. Received: ${serviceChargeRateBps}.`);
  }

  const grossTotal = netSubtotalMinorUnits + taxTotalMinorUnits;
  if (!Number.isSafeInteger(grossTotal)) {
    throw new Error('Gross total calculation resulted in unsafe integer overflow.');
  }

  let serviceCharge = 0;
  if (isServiceChargeEnabled && serviceChargeRateBps > 0) {
    const numerator = grossTotal * serviceChargeRateBps;
    if (!Number.isSafeInteger(numerator)) {
      throw new Error('Service charge numerator calculation resulted in unsafe integer overflow.');
    }
    serviceCharge = roundHalfUp(numerator, BASIS_POINTS_DIVISOR);
  }

  const grandTotal = grossTotal + serviceCharge;
  if (!Number.isSafeInteger(grandTotal)) {
    throw new Error('Grand total calculation resulted in unsafe integer overflow.');
  }

  return {
    serviceChargeMinorUnits: serviceCharge,
    grandTotalMinorUnits: grandTotal,
  };
}

export interface CartFinancialSummary {
  netSubtotalMinorUnits: number;
  taxMinorUnits: number;
  serviceChargeMinorUnits: number;
  grandTotalMinorUnits: number;
  pricesIncludeTax: boolean;
  taxRateBps: number;
  serviceChargeRateBps: number;
  isServiceChargeEnabled: boolean;
}

export interface CartFinancialLine {
  unitPriceMinorUnits: number;
  quantity: number;
}

/**
 * Computes complete financial summary for a set of cart lines and branch pricing policy.
 */
export function calculateCartFinancialSummary(
  lines: CartFinancialLine[],
  pricingPolicy: {
    pricesIncludeTax: boolean;
    defaultTaxRateBps: number;
    isServiceChargeEnabled: boolean;
    serviceChargeRateBps: number;
  }
): CartFinancialSummary {
  const {
    pricesIncludeTax,
    defaultTaxRateBps,
    isServiceChargeEnabled,
    serviceChargeRateBps,
  } = pricingPolicy;

  let totalNetSubtotal = 0;
  let totalTax = 0;

  for (const line of lines) {
    const lineFin = calculateLineItemFinancials({
      unitPriceMinorUnits: line.unitPriceMinorUnits,
      modifierTotalMinorUnits: 0,
      quantity: line.quantity,
      taxRateBps: defaultTaxRateBps,
      pricesIncludeTax,
    });

    totalNetSubtotal += lineFin.netSubtotalMinorUnits;
    totalTax += lineFin.taxMinorUnits;

    if (!Number.isSafeInteger(totalNetSubtotal) || !Number.isSafeInteger(totalTax)) {
      throw new Error('Cart financial aggregation resulted in unsafe integer overflow.');
    }
  }

  const orderTotals = calculateOrderTotals({
    netSubtotalMinorUnits: totalNetSubtotal,
    taxTotalMinorUnits: totalTax,
    isServiceChargeEnabled,
    serviceChargeRateBps,
  });

  return {
    netSubtotalMinorUnits: totalNetSubtotal,
    taxMinorUnits: totalTax,
    serviceChargeMinorUnits: orderTotals.serviceChargeMinorUnits,
    grandTotalMinorUnits: orderTotals.grandTotalMinorUnits,
    pricesIncludeTax,
    taxRateBps: defaultTaxRateBps,
    serviceChargeRateBps,
    isServiceChargeEnabled,
  };
}
