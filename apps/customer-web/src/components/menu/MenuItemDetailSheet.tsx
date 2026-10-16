import React, { useState } from 'react';
import type { RuntimeMenuItem } from '@restaurant-order/contracts';
import { BottomSheet, Button, Badge } from '@restaurant-order/ui';
import { formatCurrency, sanitizeImageUrl } from '../../utils/menuUtils';

export interface MenuItemDetailSheetProps {
  item: RuntimeMenuItem | null;
  currency: string;
  isOpen: boolean;
  onClose: () => void;
  avoidedAllergens: string[];
}

export const MenuItemDetailSheet: React.FC<MenuItemDetailSheetProps> = ({
  item,
  currency,
  isOpen,
  onClose,
  avoidedAllergens,
}) => {
  const [imageFailed, setImageFailed] = useState(false);

  if (!item) return null;

  const safeImageUrl = sanitizeImageUrl(item.imageUrl);
  const conflictingAllergens = (item.allergenTags || []).filter((a) =>
    avoidedAllergens.some((avoided) => avoided.toLowerCase() === a.toLowerCase())
  );

  return (
    <BottomSheet
      isOpen={isOpen}
      onClose={onClose}
      title={item.name}
      description={item.shortDescription || 'Ürün Bilgileri'}
    >
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--ro-space-4)',
          paddingBottom: 'var(--ro-space-4)',
        }}
        data-testid="menu-item-detail-sheet"
      >
        {/* Product Image */}
        <div
          style={{
            width: '100%',
            height: '200px',
            backgroundColor: 'var(--ro-color-surface-hover)',
            borderRadius: 'var(--ro-radius-md)',
            overflow: 'hidden',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {safeImageUrl && !imageFailed ? (
            <img
              src={safeImageUrl}
              alt={item.name}
              onError={() => setImageFailed(true)}
              style={{ width: '100%', height: '100%', objectFit: 'cover' }}
            />
          ) : (
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                color: 'var(--ro-color-text-muted)',
              }}
            >
              <span style={{ fontSize: '40px' }} role="img" aria-label="Görsel yok">
                🍽️
              </span>
              <span style={{ fontSize: 'var(--ro-font-size-xs)' }}>Lezzet Görseli</span>
            </div>
          )}
        </div>

        {/* Full description - regular text rendering (NEVER dangerouslySetInnerHTML) */}
        <div>
          <h4
            style={{
              margin: '0 0 var(--ro-space-1) 0',
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-bold)',
            }}
          >
            Açıklama
          </h4>
          <p
            style={{
              margin: 0,
              fontSize: 'var(--ro-font-size-sm)',
              color: 'var(--ro-color-text-secondary)',
              lineHeight: 1.5,
            }}
          >
            {item.fullDescription || item.shortDescription || 'Bu ürün için detaylı açıklama bulunmamaktadır.'}
          </p>
        </div>

        {/* Dietary and Allergens */}
        <div>
          <h4
            style={{
              margin: '0 0 var(--ro-space-2) 0',
              fontSize: 'var(--ro-font-size-sm)',
              fontWeight: 'var(--ro-font-weight-bold)',
            }}
          >
            Diyet ve İçerik Bilgisi
          </h4>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--ro-space-2)' }}>
            {(item.dietaryTags || []).map((tag) => (
              <Badge key={tag} variant="neutral">
                {tag}
              </Badge>
            ))}
            {(item.allergenTags || []).map((allergen) => (
              <Badge
                key={allergen}
                variant={conflictingAllergens.includes(allergen) ? 'warning' : 'neutral'}
              >
                {conflictingAllergens.includes(allergen) ? `⚠️ ${allergen}` : allergen}
              </Badge>
            ))}
          </div>
        </div>

        {/* Variants Preview if any */}
        {(item.variants || []).length > 0 && (
          <div>
            <h4
              style={{
                margin: '0 0 var(--ro-space-2) 0',
                fontSize: 'var(--ro-font-size-sm)',
                fontWeight: 'var(--ro-font-weight-bold)',
              }}
            >
              Porsiyon ve Seçenekler
            </h4>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-2)' }}>
              {item.variants.map((v) => (
                <div
                  key={v.id}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    padding: 'var(--ro-space-2) var(--ro-space-3)',
                    backgroundColor: 'var(--ro-color-surface-hover)',
                    borderRadius: 'var(--ro-radius-md)',
                    fontSize: 'var(--ro-font-size-sm)',
                  }}
                >
                  <span>{v.name}</span>
                  <span style={{ fontWeight: 'var(--ro-font-weight-bold)' }}>
                    {formatCurrency(v.absolutePriceMinorUnits, currency)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Modifier Groups Preview if any */}
        {(item.modifierGroups || []).length > 0 && (
          <div>
            <h4
              style={{
                margin: '0 0 var(--ro-space-2) 0',
                fontSize: 'var(--ro-font-size-sm)',
                fontWeight: 'var(--ro-font-weight-bold)',
              }}
            >
              Özelleştirme Grupları (Yakında)
            </h4>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-2)' }}>
              {item.modifierGroups.map((g) => (
                <div
                  key={g.id}
                  style={{
                    padding: 'var(--ro-space-2) var(--ro-space-3)',
                    backgroundColor: 'var(--ro-color-surface-hover)',
                    borderRadius: 'var(--ro-radius-md)',
                    fontSize: 'var(--ro-font-size-xs)',
                    color: 'var(--ro-color-text-secondary)',
                  }}
                >
                  <div style={{ fontWeight: 'var(--ro-font-weight-bold)', marginBottom: '4px' }}>
                    {g.name}
                  </div>
                  <div>
                    {(g.options || []).map((o) => o.name).join(', ')}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Informational note: customization in Phase 7.2 */}
        <div
          style={{
            padding: 'var(--ro-space-3)',
            backgroundColor: 'var(--ro-color-surface-hover)',
            borderRadius: 'var(--ro-radius-md)',
            fontSize: 'var(--ro-font-size-xs)',
            color: 'var(--ro-color-text-muted)',
            textAlign: 'center',
          }}
        >
          Ürün özelleştirme ve porsiyon seçimi Faz 7.2 kapsamında sunulacaktır.
        </div>

        {/* Close Button */}
        <Button
          type="button"
          variant="primary"
          size="md"
          onClick={onClose}
          data-testid="close-detail-sheet-btn"
          style={{ minHeight: '44px', width: '100%' }}
        >
          Kapat
        </Button>
      </div>
    </BottomSheet>
  );
};
