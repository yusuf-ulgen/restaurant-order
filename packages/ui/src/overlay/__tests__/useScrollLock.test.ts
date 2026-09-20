import { describe, it, expect, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useScrollLock, getScrollLockCount } from '../useScrollLock';

describe('useScrollLock', () => {
  beforeEach(() => {
    document.body.style.overflow = '';
  });

  it('locks body overflow when locked is true', () => {
    const { unmount } = renderHook(() => useScrollLock(true));

    expect(document.body.style.overflow).toBe('hidden');
    expect(getScrollLockCount()).toBe(1);

    unmount();
    expect(document.body.style.overflow).toBe('');
    expect(getScrollLockCount()).toBe(0);
  });

  it('does not lock body overflow when locked is false', () => {
    const { unmount } = renderHook(() => useScrollLock(false));

    expect(document.body.style.overflow).toBe('');
    expect(getScrollLockCount()).toBe(0);

    unmount();
    expect(document.body.style.overflow).toBe('');
  });

  it('handles nested or consecutive overlays with reference counting', () => {
    const hook1 = renderHook(() => useScrollLock(true));
    expect(document.body.style.overflow).toBe('hidden');
    expect(getScrollLockCount()).toBe(1);

    const hook2 = renderHook(() => useScrollLock(true));
    expect(document.body.style.overflow).toBe('hidden');
    expect(getScrollLockCount()).toBe(2);

    // Unmounting the first overlay keeps overflow locked because second overlay is still active
    hook1.unmount();
    expect(document.body.style.overflow).toBe('hidden');
    expect(getScrollLockCount()).toBe(1);

    // Unmounting the second overlay restores original overflow
    hook2.unmount();
    expect(document.body.style.overflow).toBe('');
    expect(getScrollLockCount()).toBe(0);
  });
});
