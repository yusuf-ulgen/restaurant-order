import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { EmptyState } from '../EmptyState';

describe('EmptyState Component', () => {
  it('renders title and description with role="status"', () => {
    render(
      <EmptyState
        title="Henüz Sipariş Yok"
        description="Masada henüz aktif bir sipariş bulunmamaktadır."
      />
    );

    const status = screen.getByRole('status');
    expect(status).toBeDefined();
    expect(screen.getByText('Henüz Sipariş Yok')).toBeDefined();
    expect(screen.getByText('Masada henüz aktif bir sipariş bulunmamaktadır.')).toBeDefined();
  });

  it('renders action button and handles clicks', () => {
    const handleAction = vi.fn();
    render(
      <EmptyState
        title="Boş Sepet"
        description="Sepetinizde ürün bulunmamaktadır."
        actionLabel="Menüye Dön"
        onAction={handleAction}
      />
    );

    const button = screen.getByText('Menüye Dön');
    expect(button).toBeDefined();
    fireEvent.click(button);
    expect(handleAction).toHaveBeenCalledTimes(1);
  });
});
