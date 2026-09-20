import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ConfirmationDialog } from '../ConfirmationDialog';

describe('ConfirmationDialog Component', () => {
  beforeEach(() => {
    document.body.style.overflow = '';
  });

  it('renders title, message, and action buttons', () => {
    render(
      <ConfirmationDialog
        isOpen={true}
        title="Masayı Kapat"
        message="Bu masayı kapatmak istediğinize emin misiniz?"
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />
    );

    expect(screen.getByText('Masayı Kapat')).toBeDefined();
    expect(
      screen.getByText('Bu masayı kapatmak istediğinize emin misiniz?')
    ).toBeDefined();
    expect(screen.getByRole('button', { name: 'Onayla' })).toBeDefined();
    expect(screen.getByRole('button', { name: 'Vazgeç' })).toBeDefined();
  });

  it('calls onConfirm when confirm button is clicked', () => {
    const handleConfirm = vi.fn();
    render(
      <ConfirmationDialog
        isOpen={true}
        title="Onay"
        message="Onay mesajı"
        onConfirm={handleConfirm}
        onCancel={vi.fn()}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Onayla' }));
    expect(handleConfirm).toHaveBeenCalledTimes(1);
  });

  it('calls onCancel when cancel button is clicked', () => {
    const handleCancel = vi.fn();
    render(
      <ConfirmationDialog
        isOpen={true}
        title="Onay"
        message="Onay mesajı"
        onConfirm={vi.fn()}
        onCancel={handleCancel}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Vazgeç' }));
    expect(handleCancel).toHaveBeenCalledTimes(1);
  });

  it('disables cancel and sets loading on confirm when isLoading is true', () => {
    const handleCancel = vi.fn();
    render(
      <ConfirmationDialog
        isOpen={true}
        title="Onay"
        message="Onay mesajı"
        isLoading={true}
        onConfirm={vi.fn()}
        onCancel={handleCancel}
      />
    );

    const cancelBtn = screen.getByRole('button', { name: 'Vazgeç' });
    expect(cancelBtn.hasAttribute('disabled')).toBe(true);

    const confirmBtn = screen.getByRole('button', { name: 'Onayla' });
    expect(confirmBtn.getAttribute('aria-busy')).toBe('true');

    // Escape should not trigger cancel during loading
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(handleCancel).not.toHaveBeenCalled();
  });
});
