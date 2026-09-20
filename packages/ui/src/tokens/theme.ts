/**
 * Theme & Token Helper Functions
 * packages/ui/src/tokens/theme.ts
 */

import type { TenantThemeOverrides } from './types';
import { tokens } from './tokens';

/**
 * Resolves a CSS variable reference string, with an optional fallback.
 */
export function resolveCssVar(varName: string, fallback?: string): string {
  const normalized = varName.startsWith('--') ? varName : `--${varName}`;
  return fallback ? `var(${normalized}, ${fallback})` : `var(${normalized})`;
}

/**
 * Returns the CSS variable for the minimum WCAG / iOS touch target (44px).
 */
export function getTouchTargetMin(): string {
  return tokens.touchTargets.min;
}

/**
 * Returns safe-area CSS variable definitions for mobile viewport insets.
 */
export function getSafeAreaInsets(): Record<'top' | 'right' | 'bottom' | 'left', string> {
  return {
    top: tokens.safeArea.top,
    right: tokens.safeArea.right,
    bottom: tokens.safeArea.bottom,
    left: tokens.safeArea.left,
  };
}

/**
 * List of centrally managed tenant theme CSS custom properties.
 */
export const MANAGED_TENANT_THEME_VARS = [
  '--ro-color-primary',
  '--ro-color-primary-hover',
  '--ro-color-primary-subtle',
  '--ro-color-secondary',
  '--ro-color-secondary-hover',
  '--ro-color-accent',
  '--ro-color-surface',
  '--ro-color-surface-hover',
  '--ro-color-bg',
  '--ro-color-focus',
  '--ro-radius-md',
  '--ro-font-sans',
] as const;

/**
 * Maps tenant customization overrides into a dictionary of CSS custom property assignments.
 */
export function createTenantTheme(overrides: TenantThemeOverrides): Record<string, string> {
  const themeVars: Record<string, string> = {};

  if (overrides.primary) themeVars['--ro-color-primary'] = overrides.primary;
  if (overrides.primaryHover) themeVars['--ro-color-primary-hover'] = overrides.primaryHover;
  if (overrides.primarySubtle) themeVars['--ro-color-primary-subtle'] = overrides.primarySubtle;
  if (overrides.secondary) themeVars['--ro-color-secondary'] = overrides.secondary;
  if (overrides.secondaryHover) themeVars['--ro-color-secondary-hover'] = overrides.secondaryHover;
  if (overrides.accent) themeVars['--ro-color-accent'] = overrides.accent;
  if (overrides.surface) themeVars['--ro-color-surface'] = overrides.surface;
  if (overrides.surfaceHover) themeVars['--ro-color-surface-hover'] = overrides.surfaceHover;
  if (overrides.bg) themeVars['--ro-color-bg'] = overrides.bg;
  if (overrides.focus) themeVars['--ro-color-focus'] = overrides.focus;
  if (overrides.radiusMd) themeVars['--ro-radius-md'] = overrides.radiusMd;
  if (overrides.fontSans) themeVars['--ro-font-sans'] = overrides.fontSans;

  return themeVars;
}

/**
 * Clears previously applied managed tenant theme CSS variables from the target element.
 * Does not touch global non-tenant CSS custom properties.
 */
export function clearTenantTheme(targetElement?: HTMLElement): void {
  if (typeof document === 'undefined') return;
  const target = targetElement ?? document.documentElement;

  for (const varName of MANAGED_TENANT_THEME_VARS) {
    target.style.removeProperty(varName);
  }
}

/**
 * Applies tenant theme CSS variable overrides to a DOM element (defaults to document.documentElement).
 * Automatically cleans up any previously applied managed tenant variables to prevent state leak.
 */
export function applyTenantTheme(
  overrides: TenantThemeOverrides,
  targetElement?: HTMLElement
): void {
  if (typeof document === 'undefined') return;

  const target = targetElement ?? document.documentElement;
  clearTenantTheme(target);

  const themeVars = createTenantTheme(overrides);

  for (const [key, value] of Object.entries(themeVars)) {
    target.style.setProperty(key, value);
  }
}
