import React, { useId } from 'react';
import { Portal } from '../overlay/Portal';
import { useScrollLock } from '../overlay/useScrollLock';
import { useFocusTrap } from '../overlay/useFocusTrap';
import { OverlayBackdrop } from '../overlay/OverlayBackdrop';
import { IconButton } from './IconButton';

export interface BottomSheetProps {
  isOpen: boolean;
  onClose: () => void;
  title?: React.ReactNode;
  description?: React.ReactNode;
  children: React.ReactNode;
  closeOnClickOutside?: boolean;
  closeOnEscape?: boolean;
  initialFocusRef?: React.RefObject<HTMLElement | null>;
  showCloseButton?: boolean;
  className?: string;
  style?: React.CSSProperties;
}

export const BottomSheet: React.FC<BottomSheetProps> = ({
  isOpen,
  onClose,
  title,
  description,
  children,
  closeOnClickOutside = true,
  closeOnEscape = true,
  initialFocusRef,
  showCloseButton = true,
  className,
  style,
}) => {
  const generatedId = useId();
  const titleId = title ? `sheet-title-${generatedId}` : undefined;
  const descriptionId = description ? `sheet-desc-${generatedId}` : undefined;

  useScrollLock(isOpen);

  const containerRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onEscape: closeOnEscape ? onClose : undefined,
    initialFocusRef,
  });

  if (!isOpen) return null;

  return (
    <Portal>
      <OverlayBackdrop
        isOpen={isOpen}
        onBackdropClick={onClose}
        closeOnClickOutside={closeOnClickOutside}
        style={{ alignItems: 'flex-end', justifyContent: 'center' }}
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
            borderTopLeftRadius: 'var(--ro-radius-2xl)',
            borderTopRightRadius: 'var(--ro-radius-2xl)',
            boxShadow: 'var(--ro-shadow-xl)',
            width: '100%',
            maxWidth: '640px',
            maxHeight: '85dvh',
            display: 'flex',
            flexDirection: 'column',
            outline: 'none',
            paddingBottom: 'max(var(--ro-space-4), var(--ro-safe-area-bottom))',
            animation: 'ro-slide-up var(--ro-duration-normal) var(--ro-ease-out)',
            ...style,
          }}
        >
          {/* Grab Handle / Indicator */}
          <div
            aria-hidden="true"
            style={{
              display: 'flex',
              justifyContent: 'center',
              paddingTop: 'var(--ro-space-2)',
              paddingBottom: 'var(--ro-space-1)',
            }}
          >
            <div
              style={{
                width: '36px',
                height: '4px',
                borderRadius: 'var(--ro-radius-full)',
                backgroundColor: 'var(--ro-color-border-strong)',
              }}
            />
          </div>

          {(title || showCloseButton) && (
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: 'var(--ro-space-3) var(--ro-space-4)',
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
        @keyframes ro-slide-up {
          from { transform: translateY(100%); }
          to { transform: translateY(0); }
        }
        @media (prefers-reduced-motion: reduce) {
          @keyframes ro-slide-up {
            from { transform: translateY(0); }
            to { transform: translateY(0); }
          }
        }
      `}</style>
    </Portal>
  );
};
