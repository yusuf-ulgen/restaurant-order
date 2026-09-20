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
    padding: '2px 8px',
    borderRadius: 'var(--ro-radius-full)',
    fontSize: '0.75rem',
    fontWeight: 600,
    fontFamily: 'var(--ro-font-sans)',
  };

  const variantStyles: Record<string, React.CSSProperties> = {
    primary: {
      backgroundColor: 'rgba(230, 57, 70, 0.1)',
      color: 'var(--ro-color-primary)',
    },
    success: {
      backgroundColor: 'rgba(42, 157, 143, 0.1)',
      color: 'var(--ro-color-success)',
    },
    warning: {
      backgroundColor: 'rgba(231, 111, 81, 0.1)',
      color: 'var(--ro-color-warning)',
    },
    danger: {
      backgroundColor: 'rgba(217, 4, 41, 0.1)',
      color: 'var(--ro-color-danger)',
    },
    neutral: {
      backgroundColor: 'var(--ro-color-border)',
      color: 'var(--ro-color-text-muted)',
    },
  };

  return (
    <span style={{ ...baseStyle, ...variantStyles[variant], ...style }} {...props}>
      {children}
    </span>
  );
};
