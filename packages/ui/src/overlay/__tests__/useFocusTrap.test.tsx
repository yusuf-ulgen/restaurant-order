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
});
