import React, { forwardRef } from 'react';

export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  options?: SelectOption[];
  isInvalid?: boolean;
  sizeVariant?: 'sm' | 'md' | 'lg';
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  {
    options,
    children,
    isInvalid = false,
    sizeVariant = 'md',
    disabled = false,
    style,
    'aria-invalid': ariaInvalid,
    ...props
  },
  ref
) {
  const sizeStyles: Record<'sm' | 'md' | 'lg', React.CSSProperties> = {
    sm: {
      padding: 'var(--ro-space-1) var(--ro-space-3)',
      fontSize: 'var(--ro-font-size-sm)',
      minHeight: 'var(--ro-touch-target-dense, 36px)',
    },
    md: {
      padding: 'var(--ro-space-2) var(--ro-space-3)',
      fontSize: 'var(--ro-font-size-base)',
      minHeight: 'var(--ro-touch-target-min, 44px)',
    },
    lg: {
      padding: 'var(--ro-space-3) var(--ro-space-4)',
      fontSize: 'var(--ro-font-size-md)',
      minHeight: '48px',
    },
  };

  const baseStyles: React.CSSProperties = {
    display: 'block',
    width: '100%',
    fontFamily: 'var(--ro-font-sans)',
    color: 'var(--ro-color-text-primary)',
    backgroundColor: disabled ? 'var(--ro-color-bg-subtle)' : 'var(--ro-color-surface)',
    border: `1px solid ${isInvalid ? 'var(--ro-color-error)' : 'var(--ro-color-border)'}`,
    borderRadius: 'var(--ro-radius-md)',
    boxSizing: 'border-box',
    cursor: disabled ? 'not-allowed' : 'pointer',
    opacity: disabled ? 0.7 : 1,
  };

  return (
    <select
      ref={ref}
      disabled={disabled}
      aria-invalid={ariaInvalid ?? (isInvalid ? 'true' : undefined)}
      style={{
        ...baseStyles,
        ...sizeStyles[sizeVariant],
        ...style,
      }}
      {...props}
    >
      {options
        ? options.map((opt) => (
            <option key={opt.value} value={opt.value} disabled={opt.disabled}>
              {opt.label}
            </option>
          ))
        : children}
    </select>
  );
});
