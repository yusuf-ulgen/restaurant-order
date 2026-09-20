import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { BottomSheet } from '../BottomSheet';

describe('BottomSheet Component', () => {
  beforeEach(() => {
    document.body.style.overflow = '';
  });

  it('does not render when isOpen is false', () => {
    render(
      <BottomSheet isOpen={false} onClose={vi.fn()}>
        <p>Sheet Content</p>
      </BottomSheet>
    );

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('renders with role="dialog", aria-modal="true", and safe-area padding', () => {
    render(
      <BottomSheet
        isOpen={true}
        onClose={vi.fn()}
        title="Sheet Title"
        description="Sheet Description"
      >
        <p>Sheet Content</p>
      </BottomSheet>
    );

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeDefined();
    expect(dialog.getAttribute('aria-modal')).toBe('true');

    const titleEl = screen.getByText('Sheet Title');
    const descEl = screen.getByText('Sheet Description');

    expect(dialog.getAttribute('aria-labelledby')).toBe(titleEl.id);
    expect(dialog.getAttribute('aria-describedby')).toBe(descEl.id);
    expect(dialog.style.paddingBottom).toContain('var(--ro-safe-area-bottom)');
  });

  it('renders drag handle indicator', () => {
    const { container } = render(
      <BottomSheet isOpen={true} onClose={vi.fn()}>
        <p>Content</p>
      </BottomSheet>
    );

    const handle = container.querySelector('[aria-hidden="true"]');
    expect(handle).toBeDefined();
  });

  it('supports long scrollable content', () => {
    const { container } = render(
      <BottomSheet isOpen={true} onClose={vi.fn()}>
        <div style={{ height: '1000px' }}>Very long content</div>
      </BottomSheet>
    );

    const scrollContainer = container.querySelector('div[style*="overflow-y: auto"]');
    expect(scrollContainer).toBeDefined();
  });

  it('closes on backdrop click and Escape key', () => {
    const handleClose = vi.fn();
    render(
      <BottomSheet isOpen={true} onClose={handleClose}>
        <p>Content</p>
      </BottomSheet>
    );

    const backdrop = screen.getByTestId('overlay-backdrop');
    fireEvent.click(backdrop);
    expect(handleClose).toHaveBeenCalledTimes(1);

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(handleClose).toHaveBeenCalledTimes(2);
  });
});
