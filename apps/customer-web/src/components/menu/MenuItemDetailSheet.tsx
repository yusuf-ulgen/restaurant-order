import React from 'react';
import {
  ItemCustomizationSheet,
  type ItemCustomizationSheetProps,
} from '../customization/ItemCustomizationSheet';

export type MenuItemDetailSheetProps = ItemCustomizationSheetProps;

/**
 * CUST-03 Product Detail & Customization Sheet.
 * Delegates to ItemCustomizationSheet for complete customization options,
 * variant selection, modifier rules, live price calculation, and add-to-cart draft.
 */
export const MenuItemDetailSheet: React.FC<MenuItemDetailSheetProps> = (props) => {
  return <ItemCustomizationSheet {...props} />;
};
