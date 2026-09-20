import React from 'react';

export interface SpinnerProps extends React.HTMLAttributes<HTMLSpanElement> {
  size?: 'sm' | 'md' | 'lg';
  label?: string;
}

export const Spinner: React.FC<SpinnerProps> = ({
  size = 'md',
  label = 'Yükleniyor...',
  style,
  ...props
}) => {
  const sizeMap: Record<'sm' | 'md' | 'lg', number> = {
    sm: 16,
    md: 24,
    lg: 32,
  };

  const dimension = sizeMap[size];

  const containerStyle: React.CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    verticalAlign: 'middle',
    width: `${dimension}px`,
    height: `${dimension}px`,
    ...style,
  };

  return (
    <span
      role="status"
      aria-label={label}
      style={containerStyle}
      {...props}
    >
      <svg
        width={dimension}
        height={dimension}
        viewBox="0 0 24 24"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        style={{
          animation: 'ro-spin 0.75s linear infinite',
        }}
        aria-hidden="true"
      >
        <circle
          cx="12"
          cy="12"
          r="10"
          stroke="currentColor"
          strokeWidth="3"
          strokeOpacity="0.2"
        />
        <path
          d="M12 2a10 10 0 0 1 10 10"
          stroke="currentColor"
          strokeWidth="3"
          strokeLinecap="round"
        />
      </svg>
      <style>{`
        @keyframes ro-spin {
          from { transform: rotate(0deg); }
          to { transform: rotate(360deg); }
        }
        @media (prefers-reduced-motion: reduce) {
          @keyframes ro-spin {
            from { transform: rotate(0deg); }
            to { transform: rotate(0deg); }
          }
        }
      `}</style>
    </span>
  );
};
