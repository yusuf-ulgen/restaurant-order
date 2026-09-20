import React, { useEffect, useRef } from 'react';
import { ToastData } from './ToastTypes';
import { IconButton } from '../components/IconButton';

export interface ToastItemProps {
  toast: ToastData;
  onDismiss: (id: string) => void;
}

export const ToastItem: React.FC<ToastItemProps> = ({ toast, onDismiss }) => {
  const { id, type, title, message, duration = 4000 } = toast;
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (duration > 0) {
      timerRef.current = setTimeout(() => {
        onDismiss(id);
      }, duration);
    }
    return () => {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }
    };
  }, [id, duration, onDismiss]);

  const handleMouseEnter = () => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  };

  const handleMouseLeave = () => {
    if (duration > 0 && !timerRef.current) {
      timerRef.current = setTimeout(() => {
        onDismiss(id);
      }, duration);
    }
  };

  const isAssertive = type === 'error' || type === 'warning';

  const typeStyles: Record<
    typeof type,
    {
      bg: string;
      border: string;
      icon: string;
      accent: string;
    }
  > = {
    success: {
      bg: 'var(--ro-color-surface)',
      border: 'var(--ro-color-success)',
      accent: 'var(--ro-color-success)',
      icon: '✓',
    },
    error: {
      bg: 'var(--ro-color-surface)',
      border: 'var(--ro-color-danger)',
      accent: 'var(--ro-color-danger)',
      icon: '✕',
    },
    warning: {
      bg: 'var(--ro-color-surface)',
      border: 'var(--ro-color-warning)',
      accent: 'var(--ro-color-warning)',
      icon: '⚠',
    },
    info: {
      bg: 'var(--ro-color-surface)',
      border: 'var(--ro-color-info, var(--ro-color-primary))',
      accent: 'var(--ro-color-info, var(--ro-color-primary))',
      icon: 'ℹ',
    },
  };

  const currentStyle = typeStyles[type];

  return (
    <div
      role={isAssertive ? 'alert' : 'status'}
      aria-live={isAssertive ? 'assertive' : 'polite'}
      data-testid={`toast-${type}`}
      data-toast-id={id}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      style={{
        pointerEvents: 'auto',
        display: 'flex',
        alignItems: 'flex-start',
        gap: 'var(--ro-space-3)',
        padding: 'var(--ro-space-3) var(--ro-space-4)',
        backgroundColor: currentStyle.bg,
        borderLeft: `4px solid ${currentStyle.accent}`,
        borderRadius: 'var(--ro-radius-md)',
        boxShadow: 'var(--ro-shadow-lg)',
        borderTop: '1px solid var(--ro-color-border)',
        borderRight: '1px solid var(--ro-color-border)',
        borderBottom: '1px solid var(--ro-color-border)',
        minWidth: '280px',
        maxWidth: '420px',
        boxSizing: 'border-box',
        animation: 'ro-toast-slide var(--ro-duration-normal) var(--ro-ease-out)',
      }}
    >
      <div
        aria-hidden="true"
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: '20px',
          height: '20px',
          borderRadius: 'var(--ro-radius-full)',
          color: currentStyle.accent,
          fontWeight: 'bold',
          flexShrink: 0,
          marginTop: '2px',
        }}
      >
        {currentStyle.icon}
      </div>

      <div style={{ flex: 1, minWidth: 0 }}>
        {title && (
          <div
            style={{
              fontWeight: 'var(--ro-font-weight-semibold)',
              fontSize: 'var(--ro-font-size-sm)',
              color: 'var(--ro-color-text-primary)',
              marginBottom: 'var(--ro-space-1)',
            }}
          >
            {title}
          </div>
        )}
        <div
          style={{
            fontSize: 'var(--ro-font-size-sm)',
            color: 'var(--ro-color-text-secondary)',
            lineHeight: 'var(--ro-line-height-normal)',
            wordBreak: 'break-word',
          }}
        >
          {message}
        </div>
      </div>

      <IconButton
        aria-label="Kapat"
        size="sm"
        variant="ghost"
        onClick={() => onDismiss(id)}
        icon={<span aria-hidden="true">✕</span>}
        style={{ flexShrink: 0, margin: '-4px -4px 0 0' }}
      />

      <style>{`
        @keyframes ro-toast-slide {
          from {
            transform: translateX(100%);
            opacity: 0;
          }
          to {
            transform: translateX(0);
            opacity: 1;
          }
        }
        @media (prefers-reduced-motion: reduce) {
          @keyframes ro-toast-slide {
            from { opacity: 0; }
            to { opacity: 1; }
          }
        }
      `}</style>
    </div>
  );
};
