export function dietaryAllergenConflict(dietary: string[], allergens: string[]): string | null {
  const set = new Set(allergens);
  if (dietary.includes('GlutenFree') && set.has('Gluten')) return 'Glutensiz etiketi gluten alerjeniyle birlikte kullanılamaz.';
  if (dietary.includes('DairyFree') && set.has('Milk')) return 'Süt ürünsüz etiketi süt alerjeniyle birlikte kullanılamaz.';
  if (dietary.includes('Vegan') && ['Milk', 'Eggs', 'Fish', 'Crustaceans', 'Molluscs'].some((tag) => set.has(tag))) return 'Vegan etiketi seçili hayvansal alerjenlerle çelişiyor.';
  if (dietary.includes('Vegetarian') && ['Fish', 'Crustaceans', 'Molluscs'].some((tag) => set.has(tag))) return 'Vejetaryen etiketi seçili deniz ürünü alerjenleriyle çelişiyor.';
  return null;
}

export function isSafeCatalogImageUrl(value: string): boolean {
  if (!value) return true;
  if (value.startsWith('/') && !value.startsWith('//') && !value.includes('..') && !value.includes('\\')) return true;
  try { return new URL(value).protocol === 'https:'; } catch { return false; }
}
