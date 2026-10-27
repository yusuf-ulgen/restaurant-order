/**
 * Re-exports shared customization utilities from @restaurant-order/contracts
 * ensuring a single authoritative source of truth for both customer and staff ordering.
 */
export {
  stripControlCharacters,
  normalizeCustomerNote,
  generateConfigurationSignature,
  type PriceCalculationResult,
  calculateItemPrice,
  type CustomizationValidationResult,
  validateCustomization,
  findModifierGroupId,
} from '@restaurant-order/contracts';
