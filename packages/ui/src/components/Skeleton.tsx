import React from 'react';

export interface SkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  variant?: 'text' | 'circular' | 'rectangular';
  width?: string | number;
  height?: string | number;
}

export const Skeleton: React.FC<SkeletonProps> = ({
  variant = 'text',
  width,
  height,
  style,
  ...props
}) => {
  const formatDimension = (val?: string | number) => {
    if (val === undefined) return undefined;
    return typeof val === 'number' ? `${val}px` : val;
  };

  const getRadius = () => {
    switch (variant) {
      case 'circular':
        return 'var(--ro-radius-full)';
      case 'rectangular':
        return 'var(--ro-radius-md)';
      case 'text':
      default:
        return 'var(--ro-radius-xs)';
    }
  };

  const getDefaultHeight = () => {
    switch (variant) {
      case 'circular':
        return formatDimension(width) || '40px';
      case 'rectangular':
        return '120px';
      case 'text':
      default:
        return '16px';
    }
  };

  const baseStyle: React.CSSProperties = {
    display: 'block',
    width: formatDimension(width) || (variant === 'circular' ? getDefaultHeight() : '100%'),
    height: formatDimension(height) || getDefaultHeight(),
    backgroundColor: 'var(--ro-color-surface-subtle)',
    borderRadius: getRadius(),
    animation: 'ro-skeleton-pulse 1.5s ease-in-out infinite',
    ...style,
  };

  return (
    <div
      aria-hidden="true"
      style={baseStyle}
      {...props}
    >
      <style>{`
        @keyframes ro-skeleton-pulse {
          0% { opacity: 1; }
          50% { opacity: 0.4; }
          100% { opacity: 1; }
        }
        @media (prefers-reduced-motion: reduce) {
          @keyframes ro-skeleton-pulse {
            0%, 100% { opacity: 0.7; }
          }
        }
      `}</style>
    </div>
  );
};
