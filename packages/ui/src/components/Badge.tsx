import React from 'react';

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: 'primary' | 'success' | 'warning' | 'danger' | 'neutral';
  children: React.ReactNode;
}

export const Badge: React.FC<BadgeProps> = ({
  variant = 'neutral',
  children,
  style,
  ...props
}) => {
  const baseStyle: React.CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    padding: 'var(--ro-space-1) var(--ro-space-2)',
    borderRadius: 'var(--ro-radius-full)',
    fontSize: 'var(--ro-font-size-xs)',
    fontWeight: 'var(--ro-font-weight-semibold)',
    fontFamily: 'var(--ro-font-sans)',
  };

  const variantStyles: Record<'primary' | 'success' | 'warning' | 'danger' | 'neutral', React.CSSProperties> = {
    primary: {
      backgroundColor: 'var(--ro-color-primary-subtle)',
      color: 'var(--ro-color-primary)',
    },
    success: {
      backgroundColor: 'var(--ro-color-success-subtle)',
      color: 'var(--ro-color-success)',
    },
    warning: {
      backgroundColor: 'var(--ro-color-warning-subtle)',
      color: 'var(--ro-color-warning)',
    },
    danger: {
      backgroundColor: 'var(--ro-color-error-subtle)',
      color: 'var(--ro-color-danger)',
    },
    neutral: {
      backgroundColor: 'var(--ro-color-border-subtle)',
      color: 'var(--ro-color-text-muted)',
    },
  };

  return (
    <span style={{ ...baseStyle, ...variantStyles[variant], ...style }} {...props}>
      {children}
    </span>
  );
};
