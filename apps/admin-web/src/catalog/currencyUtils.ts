/**
 * Currency utility functions for catalog display and minor unit conversion.
 * The system supports ISO-4217 standard two-decimal currencies (e.g., TRY, EUR, GBP, USD).
 */

export function formatCurrency(amountMinorUnits: number, currency: string = 'TRY'): string {
  const majorUnits = amountMinorUnits / 100;
  const safeCurrency = (currency || 'TRY').toUpperCase();
  try {
    return new Intl.NumberFormat('tr-TR', {
      style: 'currency',
      currency: safeCurrency,
    }).format(majorUnits);
  } catch {
    return `${majorUnits.toFixed(2)} ${safeCurrency}`;
  }
}

export function toMinorUnits(majorUnits: number | string): number {
  const parsed = typeof majorUnits === 'string' ? parseFloat(majorUnits) : majorUnits;
  return isNaN(parsed) ? 0 : Math.round(parsed * 100);
}

export function toMajorUnits(minorUnits: number): string {
  return (minorUnits / 100).toFixed(2);
}
