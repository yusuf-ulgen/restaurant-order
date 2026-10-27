import { describe, it, expect, beforeEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useAudioChime } from '../useAudioChime.js';

describe('useAudioChime', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('initializes with unmuted state by default', () => {
    const { result } = renderHook(() => useAudioChime());
    expect(result.current.isMuted).toBe(false);
  });

  it('toggles mute and persists in localStorage', () => {
    const { result } = renderHook(() => useAudioChime());

    act(() => {
      result.current.toggleMute();
    });

    expect(result.current.isMuted).toBe(true);
    expect(localStorage.getItem('restaurant_order_chime_muted')).toBe('true');

    act(() => {
      result.current.toggleMute();
    });

    expect(result.current.isMuted).toBe(false);
    expect(localStorage.getItem('restaurant_order_chime_muted')).toBe('false');
  });

  it('does not throw when playChime is called without running AudioContext', () => {
    const { result } = renderHook(() => useAudioChime());

    expect(() => {
      act(() => {
        result.current.playChime();
      });
    }).not.toThrow();
  });

  it('triggers navigator.vibrate if available and unmuted', () => {
    const vibrateMock = vi.fn();
    Object.defineProperty(navigator, 'vibrate', {
      value: vibrateMock,
      writable: true,
      configurable: true,
    });

    const { result } = renderHook(() => useAudioChime());

    act(() => {
      result.current.playChime();
    });

    expect(vibrateMock).toHaveBeenCalledWith([100, 50, 100]);
  });
});
