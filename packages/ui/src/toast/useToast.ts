import { useContext, useMemo } from 'react';
import { ToastContext } from './ToastContext';
import { ToastOptions } from './ToastTypes';

export function useToast() {
  const context = useContext(ToastContext);

  if (!context) {
    throw new Error('useToast must be used within a ToastProvider');
  }

  const { addToast, removeToast, clearToasts, toasts } = context;

  const toastHelpers = useMemo(() => {
    return {
      toasts,
      addToast,
      removeToast,
      clearToasts,
      success: (message: string, options?: ToastOptions) => addToast('success', message, options),
      error: (message: string, options?: ToastOptions) => addToast('error', message, options),
      warning: (message: string, options?: ToastOptions) => addToast('warning', message, options),
      info: (message: string, options?: ToastOptions) => addToast('info', message, options),
      dismiss: (id: string) => removeToast(id),
      clear: () => clearToasts(),
    };
  }, [addToast, removeToast, clearToasts, toasts]);

  return toastHelpers;
}
