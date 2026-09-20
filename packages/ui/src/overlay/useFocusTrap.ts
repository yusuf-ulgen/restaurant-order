import { useEffect, useRef } from 'react';

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

export interface UseFocusTrapOptions {
  isOpen: boolean;
  onEscape?: () => void;
  initialFocusRef?: React.RefObject<HTMLElement | null>;
  restoreFocus?: boolean;
}

export function useFocusTrap<T extends HTMLElement = HTMLDivElement>(
  options: UseFocusTrapOptions
): React.RefObject<T | null> {
  const containerRef = useRef<T | null>(null);
  const previouslyFocusedElementRef = useRef<HTMLElement | null>(null);

  const { isOpen, onEscape, initialFocusRef, restoreFocus = true } = options;

  useEffect(() => {
    if (!isOpen || typeof document === 'undefined') return;

    previouslyFocusedElementRef.current = document.activeElement as HTMLElement | null;

    // Focus target: initialFocusRef, or first focusable element, or container itself
    const focusTimer = setTimeout(() => {
      if (initialFocusRef?.current) {
        initialFocusRef.current.focus();
      } else {
        const container = containerRef.current;
        if (!container) return;
        const focusableElements = container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR);
        const first = focusableElements[0];
        if (first) {
          first.focus();
        } else {
          container.focus();
        }
      }
    }, 10);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && onEscape) {
        event.stopPropagation();
        onEscape();
        return;
      }

      if (event.key !== 'Tab') return;

      const container = containerRef.current;
      if (!container) return;

      const focusables = Array.from(
        container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)
      ).filter((el) => el.offsetParent !== null || el.tabIndex >= 0);

      if (focusables.length === 0) {
        event.preventDefault();
        container.focus();
        return;
      }

      const firstElement = focusables[0];
      const lastElement = focusables[focusables.length - 1];

      if (event.shiftKey) {
        if (document.activeElement === firstElement || document.activeElement === container) {
          event.preventDefault();
          lastElement?.focus();
        }
      } else {
        if (document.activeElement === lastElement) {
          event.preventDefault();
          firstElement?.focus();
        }
      }
    };

    document.addEventListener('keydown', handleKeyDown);

    return () => {
      clearTimeout(focusTimer);
      document.removeEventListener('keydown', handleKeyDown);
      if (restoreFocus && previouslyFocusedElementRef.current) {
        previouslyFocusedElementRef.current.focus();
      }
    };
  }, [isOpen, onEscape, initialFocusRef, restoreFocus]);

  return containerRef;
}
