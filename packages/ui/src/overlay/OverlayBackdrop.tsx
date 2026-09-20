import React from 'react';

export interface OverlayBackdropProps extends React.HTMLAttributes<HTMLDivElement> {
  isOpen: boolean;
  onBackdropClick?: () => void;
  closeOnClickOutside?: boolean;
  children: React.ReactNode;
  zIndex?: string | number;
}

export const OverlayBackdrop: React.FC<OverlayBackdropProps> = ({
  isOpen,
  onBackdropClick,
  closeOnClickOutside = true,
  children,
  zIndex = 'var(--ro-z-modal, 1400)',
  style,
  ...props
}) => {
  if (!isOpen) return null;

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget && closeOnClickOutside && onBackdropClick) {
      e.stopPropagation();
      onBackdropClick();
    }
  };

  const backdropStyle: React.CSSProperties = {
    position: 'fixed',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: 'rgba(0, 0, 0, 0.5)',
    zIndex,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    boxSizing: 'border-box',
    animation: 'ro-fade-in var(--ro-duration-fast) var(--ro-ease-standard)',
    ...style,
  };

  return (
    <div
      data-testid="overlay-backdrop"
      onClick={handleBackdropClick}
      style={backdropStyle}
      {...props}
    >
      {children}
      <style>{`
        @keyframes ro-fade-in {
          from { opacity: 0; }
          to { opacity: 1; }
        }
        @media (prefers-reduced-motion: reduce) {
          @keyframes ro-fade-in {
            from { opacity: 1; }
            to { opacity: 1; }
          }
        }
      `}</style>
    </div>
  );
};
