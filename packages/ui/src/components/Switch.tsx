import React, { forwardRef } from 'react';

export interface SwitchProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'onChange'> {
  checked: boolean;
  onCheckedChange?: (checked: boolean) => void;
  label?: React.ReactNode;
}

export const Switch = forwardRef<HTMLButtonElement, SwitchProps>(function Switch(
  {
    checked,
    onCheckedChange,
    label,
    disabled = false,
    onClick,
    onKeyDown,
    style,
    ...props
  },
  ref
) {
  const handleClick = (e: React.MouseEvent<HTMLButtonElement>) => {
    if (disabled) return;
    onCheckedChange?.(!checked);
    onClick?.(e);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLButtonElement>) => {
    if (disabled) return;
    if (e.key === ' ' || e.key === 'Enter') {
      e.preventDefault();
      onCheckedChange?.(!checked);
    }
    onKeyDown?.(e);
  };

  return (
    <label
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 'var(--ro-space-3)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.6 : 1,
        userSelect: 'none',
        minHeight: 'var(--ro-touch-target-min, 44px)',
        paddingTop: 'var(--ro-space-1)',
        paddingBottom: 'var(--ro-space-1)',
        ...style,
      }}
    >
      <button
        ref={ref}
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={handleClick}
        onKeyDown={handleKeyDown}
        style={{
          position: 'relative',
          display: 'inline-flex',
          alignItems: 'center',
          width: '44px',
          height: '24px',
          padding: '2px',
          borderRadius: 'var(--ro-radius-full)',
          border: '1px solid transparent',
          backgroundColor: checked ? 'var(--ro-color-primary)' : 'var(--ro-color-border-strong)',
          transition: 'background-color var(--ro-transition-fast)',
          cursor: disabled ? 'not-allowed' : 'pointer',
          outline: 'none',
        }}
        {...props}
      >
        <span
          style={{
            display: 'block',
            width: '18px',
            height: '18px',
            borderRadius: 'var(--ro-radius-full)',
            backgroundColor: 'var(--ro-color-white)',
            boxShadow: 'var(--ro-shadow-sm)',
            transform: checked ? 'translateX(20px)' : 'translateX(0)',
            transition: 'transform var(--ro-transition-fast)',
          }}
        />
      </button>
      {label && (
        <span
          style={{
            fontSize: 'var(--ro-font-size-base)',
            color: 'var(--ro-color-text-primary)',
            fontWeight: 'var(--ro-font-weight-medium)',
          }}
        >
          {label}
        </span>
      )}
    </label>
  );
});
