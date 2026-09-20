import React, { createContext, useCallback, useState } from 'react';
import { ToastContextValue, ToastData, ToastOptions, ToastType } from './ToastTypes';
import { ToastViewport } from './ToastViewport';

export const ToastContext = createContext<ToastContextValue | null>(null);

let toastCounter = 0;

export interface ToastProviderProps {
  children: React.ReactNode;
  position?: 'top-right' | 'top-left' | 'bottom-right' | 'bottom-left' | 'top-center' | 'bottom-center';
  maxToasts?: number;
}

export const ToastProvider: React.FC<ToastProviderProps> = ({
  children,
  position = 'bottom-right',
  maxToasts = 5,
}) => {
  const [toasts, setToasts] = useState<ToastData[]>([]);

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const clearToasts = useCallback(() => {
    setToasts([]);
  }, []);

  const addToast = useCallback(
    (type: ToastType, message: string, options?: ToastOptions): string => {
      const id = `toast-${Date.now()}-${++toastCounter}`;
      const newToast: ToastData = {
        id,
        type,
        message,
        title: options?.title,
        duration: options?.duration !== undefined ? options.duration : 4000,
      };

      setToasts((prev) => {
        const next = [...prev, newToast];
        if (next.length > maxToasts) {
          return next.slice(next.length - maxToasts);
        }
        return next;
      });

      return id;
    },
    [maxToasts]
  );

  return (
    <ToastContext.Provider value={{ toasts, addToast, removeToast, clearToasts }}>
      {children}
      <ToastViewport toasts={toasts} onDismiss={removeToast} position={position} />
    </ToastContext.Provider>
  );
};
