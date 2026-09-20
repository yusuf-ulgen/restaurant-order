import React from 'react';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'outline' | 'danger';
  size?: 'sm' | 'md' | 'lg';
  children: React.ReactNode;
}

export const Button: React.FC<ButtonProps> = ({
  variant = 'primary',
  size = 'md',
  children,
  style,
  disabled,
  ...props
}) => {
  const baseStyles: React.CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: 600,
    fontFamily: 'var(--ro-font-sans)',
    borderRadius: 'var(--ro-radius-md)',
    border: '1px solid transparent',
    cursor: disabled ? 'not-allowed' : 'pointer',
    opacity: disabled ? 0.6 : 1,
    transition: 'all var(--ro-transition-fast)',
    textDecoration: 'none',
  };

  const sizeStyles: Record<string, React.CSSProperties> = {
    sm: { padding: '6px 12px', fontSize: '0.875rem', minHeight: 'var(--ro-touch-target-dense, 36px)' },
    md: { padding: '10px 18px', fontSize: '1rem', minHeight: 'var(--ro-touch-target-min, 44px)' },
    lg: { padding: '14px 24px', fontSize: '1.125rem', minHeight: '48px' },
  };

  const variantStyles: Record<string, React.CSSProperties> = {
    primary: {
      backgroundColor: 'var(--ro-color-primary)',
      color: '#ffffff',
    },
    secondary: {
      backgroundColor: 'var(--ro-color-secondary)',
      color: '#ffffff',
    },
    outline: {
      backgroundColor: 'transparent',
      borderColor: 'var(--ro-color-border)',
      color: 'var(--ro-color-text)',
    },
    danger: {
      backgroundColor: 'var(--ro-color-danger)',
      color: '#ffffff',
    },
  };

  return (
    <button
      style={{ ...baseStyles, ...sizeStyles[size], ...variantStyles[variant], ...style }}
      disabled={disabled}
      {...props}
    >
      {children}
    </button>
  );
};
