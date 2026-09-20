import React from 'react';

export interface DividerProps extends React.HTMLAttributes<HTMLHRElement> {
  orientation?: 'horizontal' | 'vertical';
}

export const Divider: React.FC<DividerProps> = ({
  orientation = 'horizontal',
  style,
  ...props
}) => {
  const isHorizontal = orientation === 'horizontal';

  const baseStyle: React.CSSProperties = isHorizontal
    ? {
        display: 'block',
        width: '100%',
        height: '1px',
        border: 'none',
        backgroundColor: 'var(--ro-color-border)',
        margin: 'var(--ro-space-4) 0',
        ...style,
      }
    : {
        display: 'inline-block',
        width: '1px',
        height: '100%',
        minHeight: '16px',
        border: 'none',
        backgroundColor: 'var(--ro-color-border)',
        margin: '0 var(--ro-space-3)',
        verticalAlign: 'middle',
        ...style,
      };

  return (
    <hr
      role="separator"
      aria-orientation={orientation}
      style={baseStyle}
      {...props}
    />
  );
};
