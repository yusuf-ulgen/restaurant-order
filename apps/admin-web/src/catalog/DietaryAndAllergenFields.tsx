import React from 'react';

export const DIETARY_TAGS = ['Vegetarian', 'Vegan', 'GlutenFree', 'Halal', 'Kosher', 'DairyFree'] as const;
export const ALLERGEN_TAGS = ['Gluten', 'Crustaceans', 'Eggs', 'Fish', 'Peanuts', 'Soy', 'Milk', 'TreeNuts', 'Celery', 'Mustard', 'Sesame', 'Sulphites', 'Lupin', 'Molluscs'] as const;
const labels: Record<string, string> = {
  Vegetarian: 'Vejetaryen', Vegan: 'Vegan', GlutenFree: 'Glutensiz', Halal: 'Helal', Kosher: 'Koşer', DairyFree: 'Süt ürünsüz',
  Gluten: 'Gluten', Crustaceans: 'Kabuklular', Eggs: 'Yumurta', Fish: 'Balık', Peanuts: 'Yer fıstığı', Soy: 'Soya', Milk: 'Süt',
  TreeNuts: 'Ağaç yemişleri', Celery: 'Kereviz', Mustard: 'Hardal', Sesame: 'Susam', Sulphites: 'Sülfitler', Lupin: 'Acı bakla', Molluscs: 'Yumuşakçalar',
};

interface Props { dietaryTags: string[]; allergenTags: string[]; onDietaryChange: (tags: string[]) => void; onAllergenChange: (tags: string[]) => void; error?: string }

export const DietaryAndAllergenFields: React.FC<Props> = ({ dietaryTags, allergenTags, onDietaryChange, onAllergenChange, error }) => {
  const toggle = (items: string[], value: string, set: (next: string[]) => void) => set(items.includes(value) ? items.filter((item) => item !== value) : [...items, value]);
  return <div className="catalog-list">
    <fieldset className="catalog-panel"><legend>Beslenme etiketleri</legend><div className="catalog-tag-list">
      {DIETARY_TAGS.map((tag) => <label className="catalog-check" key={tag}><input type="checkbox" checked={dietaryTags.includes(tag)} onChange={() => toggle(dietaryTags, tag, onDietaryChange)} />{labels[tag]}</label>)}
    </div></fieldset>
    <fieldset className="catalog-panel"><legend>Alerjenler</legend><div className="catalog-tag-list">
      {ALLERGEN_TAGS.map((tag) => <label className="catalog-check" key={tag}><input type="checkbox" checked={allergenTags.includes(tag)} onChange={() => toggle(allergenTags, tag, onAllergenChange)} />{labels[tag]}</label>)}
    </div></fieldset>
    {error && <p role="alert" className="catalog-message catalog-error">{error}</p>}
  </div>;
};
