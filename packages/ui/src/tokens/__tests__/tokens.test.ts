import { describe, it, expect } from 'vitest';
import { tokens } from '../tokens';
import {
  resolveCssVar,
  getTouchTargetMin,
  getSafeAreaInsets,
  createTenantTheme,
  applyTenantTheme,
  clearTenantTheme,
  MANAGED_TENANT_THEME_VARS,
} from '../theme';

describe('Design Token System', () => {
  describe('tokens definition', () => {
    it('defines complete neutral color palette', () => {
      expect(tokens.colors.white).toBe('#ffffff');
      expect(tokens.colors.black).toBe('#000000');
      expect(tokens.colors.neutral[50]).toBe('#fafafa');
      expect(tokens.colors.neutral[900]).toBe('#18181b');
      expect(tokens.colors.neutral[950]).toBe('#09090b');
    });

    it('defines semantic status colors', () => {
      expect(tokens.colors.status.success.default).toBe('var(--ro-color-success)');
      expect(tokens.colors.status.error.default).toBe('var(--ro-color-error)');
      expect(tokens.colors.status.warning.default).toBe('var(--ro-color-warning)');
      expect(tokens.colors.status.info.default).toBe('var(--ro-color-info)');
    });

    it('defines surface, background, and border tokens', () => {
      expect(tokens.colors.bg.default).toBe('var(--ro-color-bg)');
      expect(tokens.colors.surface.default).toBe('var(--ro-color-surface)');
      expect(tokens.colors.surface.hover).toBe('var(--ro-color-surface-hover)');
      expect(tokens.colors.surface.elevated).toBe('var(--ro-color-surface-elevated)');
      expect(tokens.colors.border.default).toBe('var(--ro-color-border)');
      expect(tokens.colors.focus).toBe('var(--ro-color-focus)');
    });

    it('defines typography scale and font weights', () => {
      expect(tokens.typography.fonts.sans).toBe('var(--ro-font-sans)');
      expect(tokens.typography.sizes.base).toBe('var(--ro-font-size-base)');
      expect(tokens.typography.sizes.xl).toBe('var(--ro-font-size-xl)');
      expect(tokens.typography.weights.semibold).toBe('var(--ro-font-weight-semibold)');
      expect(tokens.typography.weights.bold).toBe('var(--ro-font-weight-bold)');
    });

    it('defines spacing scale with 4px grid steps', () => {
      expect(tokens.spacing[0]).toBe('var(--ro-space-0)');
      expect(tokens.spacing[1]).toBe('var(--ro-space-1)');
      expect(tokens.spacing[4]).toBe('var(--ro-space-4)');
      expect(tokens.spacing[8]).toBe('var(--ro-space-8)');
      expect(tokens.spacing[16]).toBe('var(--ro-space-16)');
    });

    it('defines border radius and shadow tokens', () => {
      expect(tokens.radius.sm).toBe('var(--ro-radius-sm)');
      expect(tokens.radius.md).toBe('var(--ro-radius-md)');
      expect(tokens.radius.full).toBe('var(--ro-radius-full)');
      expect(tokens.shadows.sm).toBe('var(--ro-shadow-sm)');
      expect(tokens.shadows.md).toBe('var(--ro-shadow-md)');
    });

    it('defines z-index layers and motion durations', () => {
      expect(tokens.zIndex.modal).toBe('var(--ro-z-modal)');
      expect(tokens.zIndex.toast).toBe('var(--ro-z-toast)');
      expect(tokens.motion.durations.fast).toBe('var(--ro-duration-fast)');
      expect(tokens.motion.transitions.fast).toBe('var(--ro-transition-fast)');
    });

    it('defines minimum touch targets and safe area insets', () => {
      expect(tokens.touchTargets.min).toBe('var(--ro-touch-target-min)');
      expect(tokens.safeArea.top).toBe('var(--ro-safe-area-top)');
      expect(tokens.safeArea.bottom).toBe('var(--ro-safe-area-bottom)');
    });
  });

  describe('theme helper utilities', () => {
    it('resolves CSS variable with and without prefix', () => {
      expect(resolveCssVar('--ro-color-primary')).toBe('var(--ro-color-primary)');
      expect(resolveCssVar('ro-color-primary')).toBe('var(--ro-color-primary)');
      expect(resolveCssVar('ro-color-primary', '#000')).toBe('var(--ro-color-primary, #000)');
    });

    it('returns touch target min variable', () => {
      expect(getTouchTargetMin()).toBe('var(--ro-touch-target-min)');
    });

    it('returns safe-area insets dictionary', () => {
      const insets = getSafeAreaInsets();
      expect(insets.top).toBe('var(--ro-safe-area-top)');
      expect(insets.right).toBe('var(--ro-safe-area-right)');
      expect(insets.bottom).toBe('var(--ro-safe-area-bottom)');
      expect(insets.left).toBe('var(--ro-safe-area-left)');
    });

    it('creates tenant theme dictionary from overrides', () => {
      const theme = createTenantTheme({
        primary: '#112233',
        primaryHover: '#001122',
        surface: '#ffffff',
        surfaceHover: '#f0f0f0',
        focus: '#0055ff',
        radiusMd: '10px',
      });

      expect(theme['--ro-color-primary']).toBe('#112233');
      expect(theme['--ro-color-primary-hover']).toBe('#001122');
      expect(theme['--ro-color-surface']).toBe('#ffffff');
      expect(theme['--ro-color-surface-hover']).toBe('#f0f0f0');
      expect(theme['--ro-color-focus']).toBe('#0055ff');
      expect(theme['--ro-radius-md']).toBe('10px');
      expect(theme['--ro-color-secondary']).toBeUndefined();
    });

    it('applies tenant theme to a DOM element', () => {
      const el = document.createElement('div');
      applyTenantTheme(
        {
          primary: '#ff5500',
          bg: '#f0f0f0',
        },
        el
      );

      expect(el.style.getPropertyValue('--ro-color-primary')).toBe('#ff5500');
      expect(el.style.getPropertyValue('--ro-color-bg')).toBe('#f0f0f0');
    });

    it('clears all managed tenant theme variables without touching global non-tenant styles', () => {
      const el = document.createElement('div');
      el.style.setProperty('--ro-color-primary', '#ff0000');
      el.style.setProperty('--custom-global-var', '#123456');

      clearTenantTheme(el);

      expect(el.style.getPropertyValue('--ro-color-primary')).toBe('');
      expect(el.style.getPropertyValue('--custom-global-var')).toBe('#123456');
    });

    it('cleans up Tenant A overrides when switching to Tenant B', () => {
      const el = document.createElement('div');

      // Tenant A defines primary and accent
      applyTenantTheme(
        {
          primary: '#111111',
          accent: '#aaaaaa',
        },
        el
      );
      expect(el.style.getPropertyValue('--ro-color-primary')).toBe('#111111');
      expect(el.style.getPropertyValue('--ro-color-accent')).toBe('#aaaaaa');

      // Tenant B defines only secondary (no accent)
      applyTenantTheme(
        {
          secondary: '#222222',
        },
        el
      );

      // Tenant A's primary and accent must be cleared
      expect(el.style.getPropertyValue('--ro-color-primary')).toBe('');
      expect(el.style.getPropertyValue('--ro-color-accent')).toBe('');
      // Tenant B's secondary must be set
      expect(el.style.getPropertyValue('--ro-color-secondary')).toBe('#222222');
    });

    it('exports MANAGED_TENANT_THEME_VARS list of managed css variables', () => {
      expect(MANAGED_TENANT_THEME_VARS.length).toBeGreaterThan(0);
      expect(MANAGED_TENANT_THEME_VARS).toContain('--ro-color-primary');
      expect(MANAGED_TENANT_THEME_VARS).toContain('--ro-color-secondary');
      expect(MANAGED_TENANT_THEME_VARS).toContain('--ro-color-accent');
    });
  });
});
