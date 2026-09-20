import React, { forwardRef } from 'react';
import { Spinner } from './Spinner';

export interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  'aria-label': string;
  icon: React.ReactNode;
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
}

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  {
    'aria-label': ariaLabel,
    icon,
    variant = 'ghost',
    size = 'md',
    loading = false,
    disabled = false,
    style,
    ...props
  },
  ref
) {
  const isInactive = disabled || loading;

  const sizeMap: Record<'sm' | 'md' | 'lg', { dimension: string; padding: string }> = {
    sm: { dimension: 'var(--ro-touch-target-dense, 36px)', padding: 'var(--ro-space-1)' },
    md: { dimension: 'var(--ro-touch-target-min, 44px)', padding: 'var(--ro-space-2)' },
    lg: { dimension: '48px', padding: 'var(--ro-space-3)' },
  };

  const { dimension, padding } = sizeMap[size];

  const baseStyles: React.CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: dimension,
    height: dimension,
    minWidth: dimension,
    minHeight: dimension,
    padding,
    borderRadius: 'var(--ro-radius-md)',
    border: '1px solid transparent',
    cursor: isInactive ? 'not-allowed' : 'pointer',
    opacity: isInactive ? 0.6 : 1,
    transition: 'all var(--ro-transition-fast)',
    boxSizing: 'border-box',
    userSelect: 'none',
  };

  const variantStyles: Record<'primary' | 'secondary' | 'outline' | 'ghost' | 'danger', React.CSSProperties> = {
    primary: {
      backgroundColor: 'var(--ro-color-primary)',
      color: 'var(--ro-color-white)',
    },
    secondary: {
      backgroundColor: 'var(--ro-color-secondary)',
      color: 'var(--ro-color-white)',
    },
    outline: {
      backgroundColor: 'transparent',
      borderColor: 'var(--ro-color-border)',
      color: 'var(--ro-color-text)',
    },
    ghost: {
      backgroundColor: 'transparent',
      borderColor: 'transparent',
      color: 'var(--ro-color-text)',
    },
    danger: {
      backgroundColor: 'var(--ro-color-danger)',
      color: 'var(--ro-color-white)',
    },
  };

  return (
    <button
      ref={ref}
      aria-label={ariaLabel}
      disabled={isInactive}
      aria-busy={loading ? 'true' : undefined}
      style={{
        ...baseStyles,
        ...variantStyles[variant],
        ...style,
      }}
      {...props}
    >
      {loading ? <Spinner size={size === 'lg' ? 'md' : 'sm'} aria-hidden="true" /> : icon}
    </button>
  );
});
