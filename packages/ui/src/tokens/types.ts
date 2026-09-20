/**
 * Design Token Type Definitions
 * packages/ui/src/tokens/types.ts
 */

export type NeutralColorKey =
  | 'white'
  | 'black'
  | '50'
  | '100'
  | '200'
  | '300'
  | '400'
  | '500'
  | '600'
  | '700'
  | '800'
  | '900'
  | '950';

export type StatusColorType = 'success' | 'error' | 'warning' | 'info';

export type SpacingStep = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 8 | 10 | 12 | 16;

export type RadiusStep = 'none' | 'xs' | 'sm' | 'md' | 'lg' | 'xl' | '2xl' | 'full';

export type ShadowStep = 'none' | 'xs' | 'sm' | 'md' | 'lg' | 'xl';

export type ZIndexLayer =
  | 'base'
  | 'raised'
  | 'dropdown'
  | 'sticky'
  | 'fixed'
  | 'backdrop'
  | 'modal'
  | 'popover'
  | 'toast'
  | 'tooltip';

export type FontSizeStep =
  | 'xs'
  | 'sm'
  | 'base'
  | 'md'
  | 'lg'
  | 'xl'
  | '2xl'
  | '3xl'
  | '4xl';

export type FontWeightStep = 'regular' | 'medium' | 'semibold' | 'bold';

export type LineHeightStep = 'none' | 'tight' | 'snug' | 'normal' | 'relaxed';

export type MotionDurationStep = 'instant' | 'fast' | 'normal' | 'slow';

export type MotionEasingStep = 'standard' | 'in' | 'out' | 'inOut';

export interface TenantThemeOverrides {
  primary?: string;
  primaryHover?: string;
  primarySubtle?: string;
  secondary?: string;
  secondaryHover?: string;
  accent?: string;
  surface?: string;
  bg?: string;
  radiusMd?: string;
  fontSans?: string;
}
