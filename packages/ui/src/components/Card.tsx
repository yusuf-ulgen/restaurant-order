import React from 'react';

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode;
  padding?: 'none' | 'sm' | 'md' | 'lg';
}

export const Card: React.FC<CardProps> = ({
  children,
  padding = 'md',
  style,
  ...props
}) => {
  const paddingStyles: Record<string, string> = {
    none: '0',
    sm: '12px',
    md: '20px',
    lg: '32px',
  };

  const cardStyle: React.CSSProperties = {
    backgroundColor: 'var(--ro-color-surface)',
    borderRadius: 'var(--ro-radius-lg)',
    border: '1px solid var(--ro-color-border)',
    boxShadow: 'var(--ro-shadow-sm)',
    padding: paddingStyles[padding],
    ...style,
  };

  return (
    <div style={cardStyle} {...props}>
      {children}
    </div>
  );
};
