import React, { forwardRef } from 'react';

export interface CheckboxProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label?: React.ReactNode;
  description?: string;
  isInvalid?: boolean;
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(function Checkbox(
  {
    label,
    description,
    isInvalid = false,
    disabled = false,
    id,
    style,
    onChange,
    'aria-invalid': ariaInvalid,
    ...props
  },
  ref
) {
  const inputId = id || (typeof label === 'string' ? `cb-${label.toLowerCase().replace(/\s+/g, '-')}` : undefined);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (disabled) return;
    onChange?.(e);
  };

  return (
    <label
      htmlFor={inputId}
      style={{
        display: 'inline-flex',
        alignItems: 'flex-start',
        gap: 'var(--ro-space-2)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.6 : 1,
        userSelect: 'none',
        minHeight: 'var(--ro-touch-target-min, 44px)',
        paddingTop: 'var(--ro-space-1)',
        paddingBottom: 'var(--ro-space-1)',
        ...style,
      }}
    >
      <input
        ref={ref}
        type="checkbox"
        id={inputId}
        disabled={disabled}
        onChange={handleChange}
        aria-invalid={ariaInvalid ?? (isInvalid ? 'true' : undefined)}
        style={{
          width: '18px',
          height: '18px',
          accentColor: isInvalid ? 'var(--ro-color-error)' : 'var(--ro-color-primary)',
          cursor: disabled ? 'not-allowed' : 'pointer',
          marginTop: '2px',
        }}
        {...props}
      />
      {(label || description) && (
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          {label && (
            <span
              style={{
                fontSize: 'var(--ro-font-size-base)',
                color: isInvalid ? 'var(--ro-color-error-text)' : 'var(--ro-color-text-primary)',
                fontWeight: 'var(--ro-font-weight-medium)',
                lineHeight: 'var(--ro-line-height-tight)',
              }}
            >
              {label}
            </span>
          )}
          {description && (
            <span
              style={{
                fontSize: 'var(--ro-font-size-sm)',
                color: 'var(--ro-color-text-muted)',
                marginTop: 'var(--ro-space-1)',
              }}
            >
              {description}
            </span>
          )}
        </div>
      )}
    </label>
  );
});
