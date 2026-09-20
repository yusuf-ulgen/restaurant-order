import React, { useId } from 'react';
import { Portal } from '../overlay/Portal';
import { useScrollLock } from '../overlay/useScrollLock';
import { useFocusTrap } from '../overlay/useFocusTrap';
import { OverlayBackdrop } from '../overlay/OverlayBackdrop';
import { IconButton } from './IconButton';

export interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
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

export const Modal: React.FC<ModalProps> = ({
  isOpen,
  onClose,
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
  const titleId = title ? `modal-title-${generatedId}` : undefined;
  const descriptionId = description ? `modal-desc-${generatedId}` : undefined;

  useScrollLock(isOpen);

  const containerRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onEscape: closeOnEscape ? onClose : undefined,
    initialFocusRef,
  });

  if (!isOpen) return null;

  const sizeWidths: Record<'sm' | 'md' | 'lg', string> = {
    sm: '400px',
    md: '560px',
    lg: '720px',
  };

  return (
    <Portal>
      <OverlayBackdrop
        isOpen={isOpen}
        onBackdropClick={onClose}
        closeOnClickOutside={closeOnClickOutside}
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
            borderRadius: 'var(--ro-radius-lg)',
            border: '1px solid var(--ro-color-border)',
            boxShadow: 'var(--ro-shadow-lg)',
            width: '100%',
            maxWidth: sizeWidths[size],
            margin: 'var(--ro-space-4)',
            maxHeight: 'calc(100vh - 32px)',
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
            outline: 'none',
            animation: 'ro-scale-in var(--ro-duration-fast) var(--ro-ease-standard)',
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
                      fontSize: 'var(--ro-font-size-lg)',
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
        @keyframes ro-scale-in {
          from { transform: scale(0.95); opacity: 0; }
          to { transform: scale(1); opacity: 1; }
        }
        @media (prefers-reduced-motion: reduce) {
          @keyframes ro-scale-in {
            from { transform: scale(1); opacity: 1; }
            to { transform: scale(1); opacity: 1; }
          }
        }
      `}</style>
    </Portal>
  );
};
