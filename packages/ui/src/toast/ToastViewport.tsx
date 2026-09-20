import React from 'react';
import { Portal } from '../overlay/Portal';
import { ToastData } from './ToastTypes';
import { ToastItem } from './ToastItem';

export interface ToastViewportProps {
  toasts: ToastData[];
  onDismiss: (id: string) => void;
  position?: 'top-right' | 'top-left' | 'bottom-right' | 'bottom-left' | 'top-center' | 'bottom-center';
  className?: string;
  style?: React.CSSProperties;
}

export const ToastViewport: React.FC<ToastViewportProps> = ({
  toasts,
  onDismiss,
  position = 'bottom-right',
  className,
  style,
}) => {
  if (toasts.length === 0) return null;

  const positionStyles: Record<typeof position, React.CSSProperties> = {
    'top-right': {
      top: 'max(var(--ro-space-4), var(--ro-safe-area-top))',
      right: 'max(var(--ro-space-4), var(--ro-safe-area-right))',
      alignItems: 'flex-end',
    },
    'top-left': {
      top: 'max(var(--ro-space-4), var(--ro-safe-area-top))',
      left: 'max(var(--ro-space-4), var(--ro-safe-area-left))',
      alignItems: 'flex-start',
    },
    'top-center': {
      top: 'max(var(--ro-space-4), var(--ro-safe-area-top))',
      left: '50%',
      transform: 'translateX(-50%)',
      alignItems: 'center',
    },
    'bottom-right': {
      bottom: 'max(var(--ro-space-4), var(--ro-safe-area-bottom))',
      right: 'max(var(--ro-space-4), var(--ro-safe-area-right))',
      alignItems: 'flex-end',
    },
    'bottom-left': {
      bottom: 'max(var(--ro-space-4), var(--ro-safe-area-bottom))',
      left: 'max(var(--ro-space-4), var(--ro-safe-area-left))',
      alignItems: 'flex-start',
    },
    'bottom-center': {
      bottom: 'max(var(--ro-space-4), var(--ro-safe-area-bottom))',
      left: '50%',
      transform: 'translateX(-50%)',
      alignItems: 'center',
    },
  };

  return (
    <Portal>
      <div
        role="region"
        aria-label="Bildirimler"
        data-testid="toast-viewport"
        className={className}
        style={{
          position: 'fixed',
          zIndex: 'var(--ro-z-toast, 1600)',
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--ro-space-2)',
          pointerEvents: 'none',
          maxWidth: '100%',
          padding: 'var(--ro-space-2)',
          boxSizing: 'border-box',
          ...positionStyles[position],
          ...style,
        }}
      >
        {toasts.map((toast) => (
          <ToastItem key={toast.id} toast={toast} onDismiss={onDismiss} />
        ))}
      </div>
    </Portal>
  );
};
