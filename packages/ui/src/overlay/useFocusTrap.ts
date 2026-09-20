import { useEffect, useRef, useId } from 'react';

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

interface TrapInstance {
  id: string;
  onEscape?: () => void;
  container: HTMLElement | null;
}

// Module-level stack of active focus traps to handle nested overlays and single Escape keydown correctly
const activeTrapStack: TrapInstance[] = [];

/**
 * Checks whether an element is truly visible and eligible for focus in accessibility trees.
 */
function isElementVisible(el: HTMLElement): boolean {
  if (el.hasAttribute('disabled')) return false;
  if (el.getAttribute('aria-hidden') === 'true') return false;
  if (el.closest('[aria-hidden="true"]')) return false;
  if (el.hasAttribute('inert') || el.closest('[inert]')) return false;
  if (el.hasAttribute('hidden') || el.closest('[hidden]')) return false;
  if (el.style.display === 'none' || el.style.visibility === 'hidden') return false;

  if (typeof window !== 'undefined' && window.getComputedStyle) {
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
  }

  return true;
}

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
  const trapId = useId();

  const { isOpen, onEscape, initialFocusRef, restoreFocus = true } = options;

  // Keep callback refs fresh without causing effect re-execution or premature focus teardown
  const onEscapeRef = useRef(onEscape);
  onEscapeRef.current = onEscape;

  const initialFocusRefStable = useRef(initialFocusRef);
  initialFocusRefStable.current = initialFocusRef;

  useEffect(() => {
    if (!isOpen || typeof document === 'undefined') return;

    // Capture the trigger element before moving focus into the overlay
    previouslyFocusedElementRef.current = document.activeElement as HTMLElement | null;

    // Push this trap onto the active stack
    const trapInstance: TrapInstance = {
      id: trapId,
      onEscape: () => onEscapeRef.current?.(),
      container: containerRef.current,
    };
    activeTrapStack.push(trapInstance);

    // Focus target: initialFocusRef, or first visible focusable element, or container itself
    const focusTimer = setTimeout(() => {
      if (initialFocusRefStable.current?.current) {
        initialFocusRefStable.current.current.focus();
      } else {
        const container = containerRef.current;
        if (!container) return;
        const allCandidates = Array.from(
          container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)
        );
        const visibleElements = allCandidates.filter(isElementVisible);
        const first = visibleElements[0];
        if (first) {
          first.focus();
        } else {
          container.focus();
        }
      }
    }, 10);

    const handleKeyDown = (event: KeyboardEvent) => {
      // Check if this trap is the topmost active overlay
      const isTopTrap = activeTrapStack[activeTrapStack.length - 1]?.id === trapId;

      if (event.key === 'Escape') {
        // Only the topmost overlay consumes the Escape event to prevent nested overlays closing simultaneously
        if (isTopTrap && onEscapeRef.current) {
          event.stopPropagation();
          onEscapeRef.current();
        }
        return;
      }

      if (event.key !== 'Tab' || !isTopTrap) return;

      const container = containerRef.current;
      if (!container) return;

      const allCandidates = Array.from(
        container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)
      );
      const focusables = allCandidates.filter(isElementVisible);

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

      // Remove this trap from the active stack
      const index = activeTrapStack.findIndex((t) => t.id === trapId);
      if (index !== -1) {
        activeTrapStack.splice(index, 1);
      }

      // Safely restore focus to the trigger element (or parent overlay element in nested setups)
      if (restoreFocus && previouslyFocusedElementRef.current) {
        previouslyFocusedElementRef.current.focus();
      }
    };
  }, [isOpen, restoreFocus, trapId]);

  return containerRef;
}
