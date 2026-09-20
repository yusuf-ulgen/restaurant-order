import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Drawer } from '../Drawer';

describe('Drawer Component', () => {
  beforeEach(() => {
    document.body.style.overflow = '';
  });

  it('does not render when isOpen is false', () => {
    render(
      <Drawer isOpen={false} onClose={vi.fn()}>
        <p>Drawer Content</p>
      </Drawer>
    );

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('renders drawer with right placement by default', () => {
    render(
      <Drawer
        isOpen={true}
        onClose={vi.fn()}
        title="Side Menu"
        description="Navigation options"
      >
        <p>Menu items</p>
      </Drawer>
    );

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeDefined();
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(screen.getByText('Side Menu')).toBeDefined();
  });

  it('renders drawer with left placement when specified', () => {
    render(
      <Drawer
        isOpen={true}
        onClose={vi.fn()}
        placement="left"
        title="Left Drawer"
      >
        <p>Left items</p>
      </Drawer>
    );

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeDefined();
    expect(dialog.style.animation).toContain('ro-drawer-slide-left');
  });

  it('closes on Escape and backdrop click', () => {
    const handleClose = vi.fn();
    render(
      <Drawer isOpen={true} onClose={handleClose}>
        <p>Content</p>
      </Drawer>
    );

    const backdrop = screen.getByTestId('overlay-backdrop');
    fireEvent.click(backdrop);
    expect(handleClose).toHaveBeenCalledTimes(1);

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(handleClose).toHaveBeenCalledTimes(2);
  });
});
