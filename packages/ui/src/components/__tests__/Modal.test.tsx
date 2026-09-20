import { createRef } from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { Modal } from '../Modal';
import { Drawer } from '../Drawer';

describe('Modal Component', () => {
  beforeEach(() => {
    document.body.style.overflow = '';
  });

  it('does not render when isOpen is false', () => {
    render(
      <Modal isOpen={false} onClose={vi.fn()}>
        <p>Modal Content</p>
      </Modal>
    );

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('renders with role="dialog", aria-modal="true", title and description when open', () => {
    render(
      <Modal
        isOpen={true}
        onClose={vi.fn()}
        title="Modal Title"
        description="Modal Description text"
      >
        <p>Modal Content</p>
      </Modal>
    );

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeDefined();
    expect(dialog.getAttribute('aria-modal')).toBe('true');

    const titleEl = screen.getByText('Modal Title');
    const descEl = screen.getByText('Modal Description text');

    expect(dialog.getAttribute('aria-labelledby')).toBe(titleEl.id);
    expect(dialog.getAttribute('aria-describedby')).toBe(descEl.id);
  });

  it('calls onClose when close button is clicked', () => {
    const handleClose = vi.fn();
    render(
      <Modal isOpen={true} onClose={handleClose} title="Test Modal">
        <p>Content</p>
      </Modal>
    );

    const closeBtn = screen.getByRole('button', { name: 'Kapat' });
    fireEvent.click(closeBtn);
    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape key is pressed by default', () => {
    const handleClose = vi.fn();
    render(
      <Modal isOpen={true} onClose={handleClose}>
        <p>Content</p>
      </Modal>
    );

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it('does not call onClose on Escape when closeOnEscape is false', () => {
    const handleClose = vi.fn();
    render(
      <Modal isOpen={true} onClose={handleClose} closeOnEscape={false}>
        <p>Content</p>
      </Modal>
    );

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(handleClose).not.toHaveBeenCalled();
  });

  it('calls onClose on backdrop click by default', () => {
    const handleClose = vi.fn();
    render(
      <Modal isOpen={true} onClose={handleClose}>
        <p>Content</p>
      </Modal>
    );

    const backdrop = screen.getByTestId('overlay-backdrop');
    fireEvent.click(backdrop);
    expect(handleClose).toHaveBeenCalledTimes(1);
  });

  it('does not call onClose on backdrop click when closeOnClickOutside is false', () => {
    const handleClose = vi.fn();
    render(
      <Modal isOpen={true} onClose={handleClose} closeOnClickOutside={false}>
        <p>Content</p>
      </Modal>
    );

    const backdrop = screen.getByTestId('overlay-backdrop');
    fireEvent.click(backdrop);
    expect(handleClose).not.toHaveBeenCalled();
  });

  it('does not call onClose when clicking modal content', () => {
    const handleClose = vi.fn();
    render(
      <Modal isOpen={true} onClose={handleClose}>
        <div data-testid="modal-content">Content</div>
      </Modal>
    );

    fireEvent.click(screen.getByTestId('modal-content'));
    expect(handleClose).not.toHaveBeenCalled();
  });

  it('locks body scroll when open and restores when unmounted', () => {
    const { unmount } = render(
      <Modal isOpen={true} onClose={vi.fn()}>
        <p>Content</p>
      </Modal>
    );

    expect(document.body.style.overflow).toBe('hidden');
    unmount();
    expect(document.body.style.overflow).toBe('');
  });

  it('focuses initialFocusRef element when provided', () => {
    vi.useFakeTimers();
    const inputRef = createRef<HTMLInputElement>();

    render(
      <Modal isOpen={true} onClose={vi.fn()} initialFocusRef={inputRef}>
        <button>First Button</button>
        <input ref={inputRef} data-testid="initial-input" />
      </Modal>
    );

    act(() => {
      vi.advanceTimersByTime(20);
    });

    expect(document.activeElement).toBe(screen.getByTestId('initial-input'));
    vi.useRealTimers();
  });

  describe('Accessible Name & Fallback', () => {
    it('provides fallback aria-label when no title is provided', () => {
      render(
        <Modal isOpen={true} onClose={vi.fn()}>
          <p>Content without title</p>
        </Modal>
      );

      const dialog = screen.getByRole('dialog');
      expect(dialog).toBeDefined();
      expect(dialog.getAttribute('aria-label')).toBe('İletişim Penceresi');
      expect(dialog.getAttribute('aria-labelledby')).toBeNull();
    });

    it('uses custom ariaLabel when provided without title', () => {
      render(
        <Modal isOpen={true} onClose={vi.fn()} ariaLabel="Özel Pencere">
          <p>Content</p>
        </Modal>
      );

      const dialog = screen.getByRole('dialog');
      expect(dialog.getAttribute('aria-label')).toBe('Özel Pencere');
    });
  });

  describe('Nested Modal + Drawer', () => {
    it('closes only the top overlay when Escape is pressed', () => {
      const handleCloseModal = vi.fn();
      const handleCloseDrawer = vi.fn();

      render(
        <div>
          <Modal isOpen={true} onClose={handleCloseModal} title="Ana Modal">
            <p>Modal İçeriği</p>
          </Modal>
          <Drawer isOpen={true} onClose={handleCloseDrawer} title="Üst Panel">
            <p>Drawer İçeriği</p>
          </Drawer>
        </div>
      );

      const dialogs = screen.getAllByRole('dialog');
      expect(dialogs.length).toBe(2);

      // Press Escape: only the top overlay (Drawer) must close
      fireEvent.keyDown(document, { key: 'Escape' });

      expect(handleCloseDrawer).toHaveBeenCalledTimes(1);
      expect(handleCloseModal).not.toHaveBeenCalled();
    });
  });
});
