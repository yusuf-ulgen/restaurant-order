import React, { useId } from 'react';
import { Portal } from '../overlay/Portal';
import { useScrollLock } from '../overlay/useScrollLock';
import { useFocusTrap } from '../overlay/useFocusTrap';
import { OverlayBackdrop } from '../overlay/OverlayBackdrop';
import { IconButton } from './IconButton';

export interface DrawerProps {
  isOpen: boolean;
  onClose: () => void;
  placement?: 'left' | 'right';
  title?: React.ReactNode;
  description?: React.ReactNode;
  children: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  closeOnClickOutside?: boolean;
  closeOnEscape?: boolean;
  initialFocusRef?: React.RefObject<HTMLElement | null>;
  showCloseButton?: boolean;
  className?: string;
  style?: React.CSSProperties;
}

export const Drawer: React.FC<DrawerProps> = ({
  isOpen,
  onClose,
  placement = 'right',
  title,
  description,
  children,
  size = 'md',
  closeOnClickOutside = true,
  closeOnEscape = true,
  initialFocusRef,
  showCloseButton = true,
  className,
  style,
}) => {
  const generatedId = useId();
  const titleId = title ? `drawer-title-${generatedId}` : undefined;
  const descriptionId = description ? `drawer-desc-${generatedId}` : undefined;

  useScrollLock(isOpen);

  const containerRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onEscape: closeOnEscape ? onClose : undefined,
    initialFocusRef,
  });

  if (!isOpen) return null;

  const sizeWidths: Record<'sm' | 'md' | 'lg', string> = {
    sm: '280px',
    md: '360px',
    lg: '480px',
  };

  const isRight = placement === 'right';

  return (
    <Portal>
      <OverlayBackdrop
        isOpen={isOpen}
        onBackdropClick={onClose}
        closeOnClickOutside={closeOnClickOutside}
        style={{
          alignItems: 'stretch',
          justifyContent: isRight ? 'flex-end' : 'flex-start',
        }}
      >
        <div
          ref={containerRef}
          role="dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          aria-describedby={descriptionId}
          tabIndex={-1}
          className={className}
          style={{
            backgroundColor: 'var(--ro-color-surface)',
            color: 'var(--ro-color-text-primary)',
            boxShadow: 'var(--ro-shadow-xl)',
            width: '100%',
            maxWidth: sizeWidths[size],
            height: '100dvh',
            display: 'flex',
            flexDirection: 'column',
            outline: 'none',
            borderLeft: isRight ? '1px solid var(--ro-color-border)' : 'none',
            borderRight: !isRight ? '1px solid var(--ro-color-border)' : 'none',
            animation: isRight
              ? 'ro-drawer-slide-right var(--ro-duration-normal) var(--ro-ease-out)'
              : 'ro-drawer-slide-left var(--ro-duration-normal) var(--ro-ease-out)',
            ...style,
          }}
        >
          {(title || showCloseButton) && (
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: 'var(--ro-space-4)',
                borderBottom: '1px solid var(--ro-color-border)',
              }}
            >
              <div>
                {title && (
                  <h2
                    id={titleId}
                    style={{
                      margin: 0,
                      fontSize: 'var(--ro-font-size-md)',
                      fontWeight: 'var(--ro-font-weight-semibold)',
                      color: 'var(--ro-color-text-primary)',
                    }}
                  >
                    {title}
                  </h2>
                )}
                {description && (
                  <p
                    id={descriptionId}
                    style={{
                      margin: 0,
                      marginTop: 'var(--ro-space-1)',
                      fontSize: 'var(--ro-font-size-sm)',
                      color: 'var(--ro-color-text-muted)',
                    }}
                  >
                    {description}
                  </p>
                )}
              </div>
              {showCloseButton && (
                <IconButton
                  aria-label="Kapat"
                  size="sm"
                  variant="ghost"
                  onClick={onClose}
                  icon={<span aria-hidden="true">✕</span>}
                />
              )}
            </div>
          )}

          <div
            style={{
              padding: 'var(--ro-space-4)',
              overflowY: 'auto',
              flex: 1,
            }}
          >
            {children}
          </div>
        </div>
      </OverlayBackdrop>
      <style>{`
        @keyframes ro-drawer-slide-right {
          from { transform: translateX(100%); }
          to { transform: translateX(0); }
        }
        @keyframes ro-drawer-slide-left {
          from { transform: translateX(-100%); }
          to { transform: translateX(0); }
        }
        @media (prefers-reduced-motion: reduce) {
          @keyframes ro-drawer-slide-right {
            from { transform: translateX(0); }
            to { transform: translateX(0); }
          }
          @keyframes ro-drawer-slide-left {
            from { transform: translateX(0); }
            to { transform: translateX(0); }
          }
        }
      `}</style>
    </Portal>
  );
};
