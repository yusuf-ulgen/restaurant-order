import React, { forwardRef } from 'react';
import { Spinner } from './Spinner';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  children: React.ReactNode;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    variant = 'primary',
    size = 'md',
    loading = false,
    disabled = false,
    children,
    style,
    ...props
  },
  ref
) {
  const isInactive = disabled || loading;

  const baseStyles: React.CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 'var(--ro-space-2)',
    fontWeight: 'var(--ro-font-weight-semibold)',
    fontFamily: 'var(--ro-font-sans)',
    borderRadius: 'var(--ro-radius-md)',
    border: '1px solid transparent',
    cursor: isInactive ? 'not-allowed' : 'pointer',
    opacity: isInactive ? 0.6 : 1,
    transition: 'all var(--ro-transition-fast)',
    textDecoration: 'none',
    boxSizing: 'border-box',
    userSelect: 'none',
    position: 'relative',
  };

  const sizeStyles: Record<'sm' | 'md' | 'lg', React.CSSProperties> = {
    sm: {
      padding: 'var(--ro-space-1) var(--ro-space-3)',
      fontSize: 'var(--ro-font-size-sm)',
      minHeight: 'var(--ro-touch-target-dense, 36px)',
    },
    md: {
      padding: 'var(--ro-space-2) var(--ro-space-4)',
      fontSize: 'var(--ro-font-size-base)',
      minHeight: 'var(--ro-touch-target-min, 44px)',
    },
    lg: {
      padding: 'var(--ro-space-3) var(--ro-space-6)',
      fontSize: 'var(--ro-font-size-md)',
      minHeight: '48px',
    },
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
      style={{
        ...baseStyles,
        ...sizeStyles[size],
        ...variantStyles[variant],
        ...style,
      }}
      disabled={isInactive}
      aria-busy={loading ? 'true' : undefined}
      {...props}
    >
      {loading && <Spinner size="sm" aria-hidden="true" />}
      <span>{children}</span>
    </button>
  );
});
