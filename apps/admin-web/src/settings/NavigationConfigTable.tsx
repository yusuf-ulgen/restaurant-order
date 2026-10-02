import React from 'react';
import { Card, NavigationItemOverrideContract } from '@restaurant-order/ui';
import { NAVIGATION_REGISTRY, NavigationRegistryId } from '../navigation/navigationRegistry';

export interface NavigationConfigTableProps {
  navigationOverrides: NavigationItemOverrideContract[];
  onChange: (id: NavigationRegistryId, updates: Partial<NavigationItemOverrideContract>) => void;
}

export const NavigationConfigTable: React.FC<NavigationConfigTableProps> = ({
  navigationOverrides,
  onChange,
}) => {
  const registryItems = Object.values(NAVIGATION_REGISTRY);

  return (
    <Card padding="md">
      <h3 style={{ fontSize: 'var(--ro-font-size-md)', fontWeight: 'bold', marginBottom: 'var(--ro-space-3)' }}>
        Menü & Gezinti Sıralaması
      </h3>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 'var(--ro-font-size-sm)' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid var(--ro-color-border)', textAlign: 'left' }}>
              <th style={{ padding: '8px' }}>Menü Öğesi</th>
              <th style={{ padding: '8px' }}>Görünürlük</th>
              <th style={{ padding: '8px' }}>Sıra (1-100)</th>
              <th style={{ padding: '8px' }}>Özel Etiket</th>
            </tr>
          </thead>
          <tbody>
            {registryItems.map((item) => {
              const ov = navigationOverrides.find((o) => o.id === item.id);
              const isVisible = ov?.isVisible !== undefined && ov.isVisible !== null ? ov.isVisible : true;
              const orderVal = ov?.order || item.defaultOrder;
              const labelVal = ov?.labelOverride || '';

              return (
                <tr key={item.id} style={{ borderBottom: '1px solid var(--ro-color-border)' }}>
                  <td style={{ padding: '8px', fontWeight: 500 }}>
                    {item.defaultLabel}
                  </td>
                  <td style={{ padding: '8px' }}>
                    <input
                      type="checkbox"
                      checked={isVisible}
                      onChange={(e) =>
                        onChange(item.id, { isVisible: e.target.checked })
                      }
                      aria-label={`${item.defaultLabel} görünürlüğü`}
                    />
                  </td>
                  <td style={{ padding: '8px', width: '80px' }}>
                    <input
                      type="number"
                      min="1"
                      max="100"
                      value={orderVal}
                      onChange={(e) =>
                        onChange(item.id, {
                          order: parseInt(e.target.value, 10) || 1,
                        })
                      }
                      style={{ width: '60px', padding: '4px' }}
                      aria-label={`${item.defaultLabel} sırası`}
                    />
                  </td>
                  <td style={{ padding: '8px' }}>
                    <input
                      type="text"
                      maxLength={50}
                      placeholder={item.defaultLabel}
                      value={labelVal}
                      onChange={(e) =>
                        onChange(item.id, {
                          labelOverride: e.target.value || null,
                        })
                      }
                      style={{ width: '100%', padding: '4px' }}
                      aria-label={`${item.defaultLabel} özel etiketi`}
                    />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Card>
  );
};
