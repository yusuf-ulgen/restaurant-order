import React, { useRef, useState } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { useFocusTrap } from '../useFocusTrap';

interface TestComponentProps {
  isOpen: boolean;
  onEscape?: () => void;
  useInitialRef?: boolean;
}

const TestModalComponent: React.FC<TestComponentProps> = ({
  isOpen,
  onEscape,
  useInitialRef = false,
}) => {
  const secondBtnRef = useRef<HTMLButtonElement | null>(null);
  const trapRef = useFocusTrap<HTMLDivElement>({
    isOpen,
    onEscape,
    initialFocusRef: useInitialRef ? secondBtnRef : undefined,
  });

  if (!isOpen) return null;

  return (
    <div ref={trapRef} tabIndex={-1} data-testid="trap-container">
      <button data-testid="btn-first">First</button>
      <button ref={secondBtnRef} data-testid="btn-second">
        Second
      </button>
      <button data-testid="btn-last">Last</button>
    </div>
  );
};

describe('useFocusTrap', () => {
  it('focuses the first element by default when opened', async () => {
    vi.useFakeTimers();
    render(<TestModalComponent isOpen={true} />);

    act(() => {
      vi.advanceTimersByTime(20);
    });

    expect(document.activeElement).toBe(screen.getByTestId('btn-first'));
    vi.useRealTimers();
  });

  it('focuses initialFocusRef when provided', async () => {
    vi.useFakeTimers();
    render(<TestModalComponent isOpen={true} useInitialRef={true} />);

    act(() => {
      vi.advanceTimersByTime(20);
    });

    expect(document.activeElement).toBe(screen.getByTestId('btn-second'));
    vi.useRealTimers();
  });

  it('calls onEscape when Escape key is pressed', () => {
    const handleEscape = vi.fn();
    render(<TestModalComponent isOpen={true} onEscape={handleEscape} />);

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(handleEscape).toHaveBeenCalledTimes(1);
  });

  it('wraps focus from last to first on Tab key', () => {
    render(<TestModalComponent isOpen={true} />);

    const firstBtn = screen.getByTestId('btn-first');
    const lastBtn = screen.getByTestId('btn-last');

    lastBtn.focus();
    expect(document.activeElement).toBe(lastBtn);

    fireEvent.keyDown(document, { key: 'Tab', shiftKey: false });
    expect(document.activeElement).toBe(firstBtn);
  });

  it('wraps focus from first to last on Shift+Tab key', () => {
    render(<TestModalComponent isOpen={true} />);

    const firstBtn = screen.getByTestId('btn-first');
    const lastBtn = screen.getByTestId('btn-last');

    firstBtn.focus();
    expect(document.activeElement).toBe(firstBtn);

    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(lastBtn);
  });

  it('restores focus to previously active element on close', () => {
    const Wrapper = () => {
      const [open, setOpen] = useState(false);
      return (
        <div>
          <button data-testid="trigger-btn" onClick={() => setOpen(true)}>
            Open
          </button>
          <TestModalComponent isOpen={open} onEscape={() => setOpen(false)} />
        </div>
      );
    };

    render(<Wrapper />);
    const trigger = screen.getByTestId('trigger-btn');
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    fireEvent.click(trigger);
    expect(screen.getByTestId('trap-container')).toBeDefined();

    // Close
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByTestId('trap-container')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('does not teardown or prematurely restore focus when onEscape callback identity changes on rerender', async () => {
    vi.useFakeTimers();

    const RerenderWrapper = () => {
      const [count, setCount] = useState(0);
      return (
        <div>
          <button data-testid="rerender-btn" onClick={() => setCount((c) => c + 1)}>
            Count: {count}
          </button>
          <TestModalComponent
            isOpen={true}
            // New inline function on every render
            onEscape={() => {}}
          />
        </div>
      );
    };

    render(<RerenderWrapper />);

    act(() => {
      vi.advanceTimersByTime(20);
    });

    const firstBtn = screen.getByTestId('btn-first');
    expect(document.activeElement).toBe(firstBtn);

    // Focus second button inside trap
    const secondBtn = screen.getByTestId('btn-second');
    secondBtn.focus();
    expect(document.activeElement).toBe(secondBtn);

    // Trigger parent rerender with new callback reference
    const rerenderBtn = screen.getByTestId('rerender-btn');
    fireEvent.click(rerenderBtn);

    // Focus must remain on the element inside the trap, NOT jump back to trigger or reset
    expect(document.activeElement).toBe(secondBtn);

    vi.useRealTimers();
  });

  it('skips hidden, aria-hidden, and disabled elements when trapping focus', () => {
    const ComponentWithHiddenElements = () => {
      const trapRef = useFocusTrap<HTMLDivElement>({ isOpen: true });
      return (
        <div ref={trapRef} tabIndex={-1} data-testid="hidden-trap">
          <button data-testid="visible-1">Visible 1</button>
          <button data-testid="hidden-display" style={{ display: 'none' }}>
            Hidden Display
          </button>
          <button data-testid="aria-hidden-btn" aria-hidden="true">
            Aria Hidden
          </button>
          <button data-testid="disabled-btn" disabled>
            Disabled
          </button>
          <div aria-hidden="true">
            <button data-testid="nested-in-aria-hidden">Inside Hidden Container</button>
          </div>
          <button data-testid="visible-2">Visible 2</button>
        </div>
      );
    };

    render(<ComponentWithHiddenElements />);

    const visible1 = screen.getByTestId('visible-1');
    const visible2 = screen.getByTestId('visible-2');

    visible1.focus();
    expect(document.activeElement).toBe(visible1);

    // Shift+Tab from visible1 wraps to the last visible element (visible2), skipping all hidden/disabled elements
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(visible2);

    // Tab from visible2 wraps back to first visible element (visible1)
    fireEvent.keyDown(document, { key: 'Tab', shiftKey: false });
    expect(document.activeElement).toBe(visible1);
  });

  it('handles nested overlays: single Escape closes only top overlay and restores focus to parent overlay', async () => {
    vi.useFakeTimers();

    const NestedOverlays = () => {
      const [isParentOpen, setIsParentOpen] = useState(false);
      const [isChildOpen, setIsChildOpen] = useState(false);

      const parentTrapRef = useFocusTrap<HTMLDivElement>({
        isOpen: isParentOpen,
        onEscape: () => setIsParentOpen(false),
      });

      const childTrapRef = useFocusTrap<HTMLDivElement>({
        isOpen: isChildOpen,
        onEscape: () => setIsChildOpen(false),
      });

      return (
        <div>
          <button data-testid="open-parent" onClick={() => setIsParentOpen(true)}>
            Open Parent
          </button>

          {isParentOpen && (
            <div ref={parentTrapRef} tabIndex={-1} data-testid="parent-overlay">
              <button data-testid="parent-btn-1">Parent 1</button>
              <button data-testid="open-child" onClick={() => setIsChildOpen(true)}>
                Open Child
              </button>
            </div>
          )}

          {isChildOpen && (
            <div ref={childTrapRef} tabIndex={-1} data-testid="child-overlay">
              <button data-testid="child-btn">Child Action</button>
            </div>
          )}
        </div>
      );
    };

    render(<NestedOverlays />);

    // 1. Open parent overlay
    const openParentBtn = screen.getByTestId('open-parent');
    openParentBtn.focus();
    fireEvent.click(openParentBtn);

    act(() => {
      vi.advanceTimersByTime(20);
    });
    expect(screen.getByTestId('parent-overlay')).toBeDefined();

    // 2. Open child overlay from parent trigger
    const openChildBtn = screen.getByTestId('open-child');
    openChildBtn.focus();
    fireEvent.click(openChildBtn);

    act(() => {
      vi.advanceTimersByTime(20);
    });
    expect(screen.getByTestId('child-overlay')).toBeDefined();
    expect(document.activeElement).toBe(screen.getByTestId('child-btn'));

    // 3. Press Escape: ONLY the child overlay must close!
    fireEvent.keyDown(document, { key: 'Escape' });

    // Child is closed
    expect(screen.queryByTestId('child-overlay')).toBeNull();
    // Parent remains open!
    expect(screen.getByTestId('parent-overlay')).toBeDefined();
    // Focus is restored to the child trigger inside the parent overlay!
    expect(document.activeElement).toBe(openChildBtn);

    // 4. Press Escape again: now parent closes!
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByTestId('parent-overlay')).toBeNull();
    // Focus restored to initial trigger on page
    expect(document.activeElement).toBe(openParentBtn);

    vi.useRealTimers();
  });
});
