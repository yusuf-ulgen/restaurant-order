import React from 'react';

export interface PageContainerProps {
  children: React.ReactNode;
  maxWidth?: 'sm' | 'md' | 'lg' | 'xl' | 'full';
  padding?: 'none' | 'sm' | 'md' | 'lg';
  as?: 'main' | 'div' | 'section';
  className?: string;
  style?: React.CSSProperties;
  id?: string;
}

export const PageContainer: React.FC<PageContainerProps> = ({
  children,
  maxWidth = 'xl',
  padding = 'md',
  as: Component = 'div',
  className,
  style,
  id,
}) => {
  const maxWidths: Record<typeof maxWidth, string> = {
    sm: '640px',
    md: '768px',
    lg: '1024px',
    xl: '1280px',
    full: '100%',
  };

  const paddings: Record<typeof padding, string> = {
    none: '0',
    sm: 'var(--ro-space-2) var(--ro-space-3)',
    md: 'var(--ro-space-4)',
    lg: 'var(--ro-space-6)',
  };

  return (
    <Component
      id={id}
      data-testid="page-container"
      className={className}
      style={{
        width: '100%',
        maxWidth: maxWidths[maxWidth],
        marginLeft: 'auto',
        marginRight: 'auto',
        padding: paddings[padding],
        boxSizing: 'border-box',
        overflowX: 'hidden',
        ...style,
      }}
    >
      {children}
    </Component>
  );
};
