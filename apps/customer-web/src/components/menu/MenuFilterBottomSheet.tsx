import React from 'react';
import { BottomSheet, Button } from '@restaurant-order/ui';
import { KNOWN_ALLERGENS, KNOWN_DIETARY_TAGS } from '../../utils/menuUtils';

export interface MenuFilterBottomSheetProps {
  isOpen: boolean;
  onClose: () => void;
  selectedDietaryTags: string[];
  onToggleDietaryTag: (tag: string) => void;
  avoidedAllergens: string[];
  onToggleAllergen: (allergen: string) => void;
  onResetFilters: () => void;
}

export const MenuFilterBottomSheet: React.FC<MenuFilterBottomSheetProps> = ({
  isOpen,
  onClose,
  selectedDietaryTags,
  onToggleDietaryTag,
  avoidedAllergens,
  onToggleAllergen,
  onResetFilters,
}) => {
  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={onClose}
      title="Diyet ve Alerjen Tercihleri"
      description="Menüdeki ürünleri diyet tercihlerinize ve kaçınmak istediğiniz alerjenlere göre filtreleyin."
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--ro-space-4)',
          paddingBottom: 'var(--ro-space-4)',
        }}
        data-testid="menu-filter-bottom-sheet"
      >
        {/* Dietary preferences section */}
        <div>
          <h3
            style={{
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
              margin: '0 0 var(--ro-space-2) 0',
            }}
          >
            Diyet Tercihleri
          </h3>
          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              gap: 'var(--ro-space-2)',
            }}
          >
            {KNOWN_DIETARY_TAGS.map((tag) => {
              const isSelected = selectedDietaryTags.includes(tag.id);
              return (
                <button
                  key={tag.id}
                  type="button"
                  aria-pressed={isSelected}
                  onClick={() => onToggleDietaryTag(tag.id)}
                  data-testid={`dietary-filter-${tag.id}`}
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 'var(--ro-space-1)',
                    padding: 'var(--ro-space-2) var(--ro-space-3)',
                    borderRadius: 'var(--ro-radius-full)',
                    fontSize: 'var(--ro-font-size-sm)',
                    fontWeight: isSelected ? 'var(--ro-font-weight-semibold)' : 'var(--ro-font-weight-regular)',
                    border: '1px solid',
                    borderColor: isSelected ? 'var(--ro-color-text-primary)' : 'var(--ro-color-border)',
                    backgroundColor: isSelected ? 'var(--ro-color-text-primary)' : 'var(--ro-color-surface)',
                    color: isSelected ? 'var(--ro-color-surface)' : 'var(--ro-color-text-primary)',
                    cursor: 'pointer',
                    minHeight: '44px',
                    transition: 'all var(--ro-transition-fast)',
                  }}
                >
                  {isSelected && <span aria-hidden="true">✓ </span>}
                  <span>{tag.label}</span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Avoided allergens section */}
        <div>
          <h3
            style={{
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
              margin: '0 0 var(--ro-space-2) 0',
            }}
          >
            Kaçınılacak Alerjenler
          </h3>
          <p
            style={{
              fontSize: 'var(--ro-font-size-xs)',
              color: 'var(--ro-color-text-muted)',
              margin: '0 0 var(--ro-space-2) 0',
            }}
          >
            Seçtiğiniz alerjenleri içeren lezzetler listede belirgin uyarıyla işaretlenir.
          </p>
          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              gap: 'var(--ro-space-2)',
            }}
          >
            {KNOWN_ALLERGENS.map((allergen) => {
              const isAvoided = avoidedAllergens.includes(allergen.id);
              return (
                <button
                  key={allergen.id}
                  type="button"
                  aria-pressed={isAvoided}
                  onClick={() => onToggleAllergen(allergen.id)}
                  data-testid={`allergen-filter-${allergen.id}`}
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 'var(--ro-space-1)',
                    padding: 'var(--ro-space-2) var(--ro-space-3)',
                    borderRadius: 'var(--ro-radius-full)',
                    fontSize: 'var(--ro-font-size-sm)',
                    fontWeight: isAvoided ? 'var(--ro-font-weight-semibold)' : 'var(--ro-font-weight-regular)',
                    border: '1px solid',
                    borderColor: isAvoided ? 'var(--ro-color-warning, #f59e0b)' : 'var(--ro-color-border)',
                    backgroundColor: isAvoided ? 'var(--ro-color-warning-subtle, #fef3c7)' : 'var(--ro-color-surface)',
                    color: isAvoided ? 'var(--ro-color-warning-text, #92400e)' : 'var(--ro-color-text-primary)',
                    cursor: 'pointer',
                    minHeight: '44px',
                    transition: 'all var(--ro-transition-fast)',
                  }}
                >
                  {isAvoided && <span aria-hidden="true">⚠️ </span>}
                  <span>{allergen.label}</span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Responsible allergen disclaimer */}
        <div
          role="note"
          aria-label="Alerjen uyarısı ve bilgilendirme"
          style={{
            padding: 'var(--ro-space-3)',
            backgroundColor: 'var(--ro-color-surface-hover)',
            borderRadius: 'var(--ro-radius-md)',
            borderLeft: '4px solid var(--ro-color-warning, #f59e0b)',
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-secondary)',
            lineHeight: 1.5,
          }}
        >
          <strong>Önemli Bilgilendirme:</strong> Seçtiğiniz alerjenleri içeren ürünler menüde belirgin biçimde etiketlenir. Mutfak ortamındaki olası çapraz temas veya bulaşma risklerine karşı ciddi alerjileriniz varsa lütfen sipariş vermeden önce servis görevlimize bilgi veriniz.
        </div>

        {/* Footer actions */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 'var(--ro-space-3)',
            paddingTop: 'var(--ro-space-2)',
            borderTop: '1px solid var(--ro-color-border)',
          }}
        >
          <Button
            type="button"
            variant="outline"
            size="md"
            onClick={onResetFilters}
            data-testid="reset-filter-sheet-btn"
            style={{ minHeight: '44px' }}
          >
            Sıfırla
          </Button>

          <Button
            type="button"
            variant="primary"
            size="md"
            onClick={onClose}
            data-testid="apply-filter-sheet-btn"
            style={{ minHeight: '44px', flex: 1 }}
          >
            Uygula
          </Button>
        </div>
      </div>
    </BottomSheet>
  );
};
