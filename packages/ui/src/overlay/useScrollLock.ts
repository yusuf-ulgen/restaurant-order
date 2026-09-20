import { useEffect } from 'react';

let lockCount = 0;
let originalOverflow = '';

/**
 * Safely locks background document.body scroll while an overlay is active.
 * Uses a reference counter so nested or consecutive overlays preserve lock state.
 */
export function useScrollLock(locked: boolean): void {
  useEffect(() => {
    if (!locked || typeof document === 'undefined') return;

    if (lockCount === 0) {
      originalOverflow = document.body.style.overflow;
      document.body.style.overflow = 'hidden';
    }
    lockCount++;

    return () => {
      lockCount = Math.max(0, lockCount - 1);
      if (lockCount === 0) {
        document.body.style.overflow = originalOverflow;
      }
    };
  }, [locked]);
}

/**
 * Returns current lock count (useful for testing)
 */
export function getScrollLockCount(): number {
  return lockCount;
}

/**
 * Resets the scroll lock counter and restores document body overflow.
 * Useful for nested overlay teardown synchronization and test cleanups.
 */
export function resetScrollLock(): void {
  lockCount = 0;
  if (typeof document !== 'undefined') {
    document.body.style.overflow = originalOverflow || '';
  }
}
