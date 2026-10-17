/**
 * Turkish-aware text normalization for search queries and product text matching.
 * Converts characters to lowercase and normalizes Turkish diacritics for insensitive matching.
 */
export function normalizeTurkishText(text: string): string {
  if (!text) return '';
  return text
    .replace(/İ/g, 'i')
    .replace(/I/g, 'ı')
    .toLocaleLowerCase('tr-TR')
    .replace(/ç/g, 'c')
    .replace(/ğ/g, 'g')
    .replace(/ı/g, 'i')
    .replace(/ö/g, 'o')
    .replace(/ş/g, 's')
    .replace(/ü/g, 'u')
    .trim();
}

/**
 * Validates and sanitizes image URLs according to strict security policy.
 * Only allows 'https://' URLs or absolute safe relative paths (e.g., '/images/...').
 * Strictly rejects 'javascript:', 'data:', protocol-relative '//', and invalid schemes.
 */
export function sanitizeImageUrl(url: string | null | undefined): string | null {
  if (!url || typeof url !== 'string') return null;
  const trimmed = url.trim();
  if (!trimmed) return null;

  const lower = trimmed.toLowerCase();
  if (
    lower.startsWith('javascript:') ||
    lower.startsWith('data:') ||
    lower.startsWith('vbscript:') ||
    lower.startsWith('file:') ||
    trimmed.startsWith('//')
  ) {
    return null;
  }

  if (lower.startsWith('https://')) {
    try {
      const parsed = new URL(trimmed);
      if (parsed.protocol === 'https:') {
        return trimmed;
      }
    } catch {
      return null;
    }
  }

  // Safe relative paths starting with a single '/'
  if (trimmed.startsWith('/') && !trimmed.startsWith('//')) {
    return trimmed;
  }

  return null;
}

/**
 * Formats minor units (e.g., kuruş/cents) to formatted currency string using Intl.NumberFormat.
 */
export function formatCurrency(
  minorUnits: number,
  currencyCode: string = 'TRY',
  locale: string = 'tr-TR'
): string {
  if (typeof minorUnits !== 'number' || !Number.isFinite(minorUnits)) {
    return '0,00 ₺';
  }

  const majorUnits = minorUnits / 100;
  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: currencyCode || 'TRY',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(majorUnits);
  } catch {
    return `${majorUnits.toFixed(2)} ${currencyCode}`;
  }
}

/**
 * Common allergen labels and Turkish translations.
 */
export const KNOWN_ALLERGENS: { id: string; label: string }[] = [
  { id: 'Gluten', label: 'Gluten' },
  { id: 'Lactose', label: 'Süt / Laktoz' },
  { id: 'Nuts', label: 'Kuruyemiş' },
  { id: 'Peanuts', label: 'Yer Fıstığı' },
  { id: 'Eggs', label: 'Yumurta' },
  { id: 'Fish', label: 'Balık' },
  { id: 'Shellfish', label: 'Kabuklu Deniz Ürünü' },
  { id: 'Soy', label: 'Soya' },
  { id: 'Sesame', label: 'Susam' },
  { id: 'Celery', label: 'Kereviz' },
  { id: 'Mustard', label: 'Hardal' },
];

/**
 * Common dietary tags and Turkish translations.
 */
export const KNOWN_DIETARY_TAGS: { id: string; label: string }[] = [
  { id: 'Vegan', label: 'Vegan' },
  { id: 'Vegetarian', label: 'Vejetaryen' },
  { id: 'GlutenFree', label: 'Glutensiz' },
  { id: 'Halal', label: 'Helal' },
  { id: 'DairyFree', label: 'Sütsüz' },
  { id: 'NutFree', label: 'Fıstıksız' },
];
