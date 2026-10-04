using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Domain validator enforcing strict dietary and allergen consistency rules.
/// Prevents contradictions such as GlutenFree items containing Gluten, or Vegan items containing animal products.
/// </summary>
public static class DietaryAndAllergenValidator
{
    public static void ValidateConsistency(
        ISet<DietaryTag> dietaryTags,
        ISet<AllergenTag> allergenTags)
    {
        if (dietaryTags.Contains(DietaryTag.GlutenFree) && allergenTags.Contains(AllergenTag.Gluten))
        {
            throw new DomainException("Contradiction: Item cannot be labeled as 'GlutenFree' while containing 'Gluten' allergen.");
        }

        if (dietaryTags.Contains(DietaryTag.DairyFree) && allergenTags.Contains(AllergenTag.Milk))
        {
            throw new DomainException("Contradiction: Item cannot be labeled as 'DairyFree' while containing 'Milk' allergen.");
        }

        if (dietaryTags.Contains(DietaryTag.Vegan))
        {
            if (allergenTags.Contains(AllergenTag.Milk))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegan' while containing 'Milk' allergen.");
            }
            if (allergenTags.Contains(AllergenTag.Eggs))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegan' while containing 'Eggs' allergen.");
            }
            if (allergenTags.Contains(AllergenTag.Fish))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegan' while containing 'Fish' allergen.");
            }
            if (allergenTags.Contains(AllergenTag.Crustaceans))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegan' while containing 'Crustaceans' allergen.");
            }
            if (allergenTags.Contains(AllergenTag.Molluscs))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegan' while containing 'Molluscs' allergen.");
            }
        }

        if (dietaryTags.Contains(DietaryTag.Vegetarian))
        {
            if (allergenTags.Contains(AllergenTag.Fish))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegetarian' while containing 'Fish' allergen.");
            }
            if (allergenTags.Contains(AllergenTag.Crustaceans))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegetarian' while containing 'Crustaceans' allergen.");
            }
            if (allergenTags.Contains(AllergenTag.Molluscs))
            {
                throw new DomainException("Contradiction: Item cannot be labeled as 'Vegetarian' while containing 'Molluscs' allergen.");
            }
        }
    }
}
