import React, { useState } from 'react';
import type { RuntimeMenuItem } from '@restaurant-order/contracts';
import { Card, Badge, Button } from '@restaurant-order/ui';
import { formatCurrency, sanitizeImageUrl } from '../../utils/menuUtils';

export interface MenuItemCardProps {
  item: RuntimeMenuItem;
  currency: string;
  avoidedAllergens: string[];
  onSelect: (item: RuntimeMenuItem) => void;
}

export const MenuItemCard: React.FC<MenuItemCardProps> = ({
  item,
  currency,
  avoidedAllergens,
  onSelect,
}) => {
  const [imageFailed, setImageFailed] = useState(false);
  const safeImageUrl = sanitizeImageUrl(item.imageUrl);

  // Calculate starting price across variants if any exist
  const variantPrices = (item.variants || [])
    .filter((v) => v.isAvailable)
    .map((v) => v.absolutePriceMinorUnits);

  const minPrice = variantPrices.length > 0
    ? Math.min(item.basePriceMinorUnits, ...variantPrices)
    : item.basePriceMinorUnits;

  const hasMultiplePrices = variantPrices.length > 1 && Math.max(...variantPrices) > minPrice;

  // Check allergen collision
  const conflictingAllergens = (item.allergenTags || []).filter((a) =>
    avoidedAllergens.some((avoided) => avoided.toLowerCase() === a.toLowerCase())
  );
  const hasAllergenWarning = conflictingAllergens.length > 0;

  // Format restock time if provided
  let formattedRestockTime: string | null = null;
  if (!item.isAvailable && item.expectedAvailableAtUtc) {
    try {
      const restockDate = new Date(item.expectedAvailableAtUtc);
      formattedRestockTime = restockDate.toLocaleTimeString('tr-TR', {
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      formattedRestockTime = null;
    }
  }

  return (
    <Card
      padding="none"
      className="menu-item-card"
      data-testid={`menu-item-card-${item.id}`}
      style={{
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        border: hasAllergenWarning ? '1px solid var(--ro-color-warning, #f59e0b)' : '1px solid var(--ro-color-border)',
        opacity: item.isAvailable ? 1 : 0.75,
        backgroundColor: 'var(--ro-color-surface)',
        transition: 'transform var(--ro-transition-fast), box-shadow var(--ro-transition-fast)',
      }}
    >
      {/* Product Image or Fallback */}
      <div
        style={{
          width: '100%',
          height: '160px',
          backgroundColor: 'var(--ro-color-surface-hover)',
          position: 'relative',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          overflow: 'hidden',
        }}
      >
        {safeImageUrl && !imageFailed ? (
          <img
            src={safeImageUrl}
            alt={item.name}
            onError={() => setImageFailed(true)}
            data-testid={`item-image-${item.id}`}
            style={{
              width: '100%',
              height: '100%',
              objectFit: 'cover',
            }}
          />
        ) : (
          <div
            data-testid={`item-image-fallback-${item.id}`}
            style={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              color: 'var(--ro-color-text-muted)',
              fontSize: 'var(--ro-font-size-xs)',
              gap: 'var(--ro-space-1)',
            }}
          >
            <span style={{ fontSize: '32px' }} role="img" aria-label="Görsel bulunmuyor">
              🍽️
            </span>
            <span>Lezzet Görseli</span>
          </div>
        )}

        {/* Availability Badge */}
        {!item.isAvailable && (
          <div
            style={{
              position: 'absolute',
              top: '8px',
              left: '8px',
            }}
          >
            <Badge variant="danger">
              {formattedRestockTime ? `Tükendi (Tahmini ${formattedRestockTime})` : 'Tükendi'}
            </Badge>
          </div>
        )}

        {/* Spicy Indicator */}
        {item.spicyLevel > 0 && (
          <div
            style={{
              position: 'absolute',
              top: '8px',
              right: '8px',
              backgroundColor: 'rgba(0, 0, 0, 0.7)',
              borderRadius: 'var(--ro-radius-full)',
              padding: '2px 8px',
              color: '#ffffff',
              fontSize: '11px',
              fontWeight: 'var(--ro-font-weight-semibold)',
            }}
            title={`Acı Seviyesi: ${item.spicyLevel}`}
            data-testid={`spicy-badge-${item.id}`}
          >
            {'🌶️'.repeat(Math.min(item.spicyLevel, 3))}
          </div>
        )}
      </div>

      {/* Item Body */}
      <div
        style={{
          padding: 'var(--ro-space-3)',
          display: 'flex',
          flexDirection: 'column',
          flex: 1,
          justifyContent: 'space-between',
          gap: 'var(--ro-space-2)',
        }}
      >
        <div>
          {/* Item Title */}
          <h3
            style={{
              margin: '0 0 var(--ro-space-1) 0',
              fontSize: 'var(--ro-font-size-base)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
              lineHeight: 1.3,
            }}
          >
            {item.name}
          </h3>

          {/* Short Description */}
          {item.shortDescription && (
            <p
              style={{
                margin: '0 0 var(--ro-space-2) 0',
                fontSize: 'var(--ro-font-size-xs)',
                color: 'var(--ro-color-text-muted)',
                lineHeight: 1.4,
                display: '-webkit-box',
                WebkitLineClamp: 2,
                WebkitBoxOrient: 'vertical',
                overflow: 'hidden',
              }}
            >
              {item.shortDescription}
            </p>
          )}

          {/* Dietary & Allergen Collisions */}
          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              gap: '4px',
              marginBottom: 'var(--ro-space-2)',
            }}
          >
            {(item.dietaryTags || []).map((tag) => (
              <Badge key={tag} variant="neutral" style={{ fontSize: '10px', padding: '1px 6px' }}>
                {tag}
              </Badge>
            ))}

            {hasAllergenWarning && (
              <span
                data-testid={`allergen-warning-${item.id}`}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  fontSize: '11px',
                  fontWeight: 'var(--ro-font-weight-semibold)',
                  color: 'var(--ro-color-warning-text, #92400e)',
                  backgroundColor: 'var(--ro-color-warning-subtle, #fef3c7)',
                  padding: '2px 6px',
                  borderRadius: 'var(--ro-radius-sm)',
                }}
              >
                ⚠️ {conflictingAllergens.join(', ')} içerir
              </span>
            )}
          </div>
        </div>

        {/* Pricing & Detail Action Button */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            paddingTop: 'var(--ro-space-2)',
            borderTop: '1px solid var(--ro-color-border-subtle, #f3f4f6)',
          }}
        >
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            <span
              style={{
                fontSize: 'var(--ro-font-size-sm)',
                fontWeight: 'var(--ro-font-weight-bold)',
                color: 'var(--ro-color-text-primary)',
              }}
              data-testid={`item-price-${item.id}`}
            >
              {formatCurrency(minPrice, currency)}
            </span>
            {hasMultiplePrices && (
              <span
                style={{
                  fontSize: '10px',
                  color: 'var(--ro-color-text-muted)',
                }}
              >
                başlangıç fiyatı
              </span>
            )}
          </div>

          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => onSelect(item)}
            data-testid={`inspect-item-btn-${item.id}`}
            aria-label={`${item.name} ürün detayını incele`}
            style={{
              minHeight: '44px',
              paddingLeft: 'var(--ro-space-3)',
              paddingRight: 'var(--ro-space-3)',
            }}
          >
            İncele
          </Button>
        </div>
      </div>
    </Card>
  );
};
