import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ErrorState } from '../ErrorState';

describe('ErrorState Component', () => {
  it('renders with role="alert" and displays title/message', () => {
    render(
      <ErrorState
        title="Bağlantı Hatası"
        message="Sunucu ile iletişim kurulamadı. Lütfen internet bağlantınızı kontrol edin."
      />
    );

    const alert = screen.getByRole('alert');
    expect(alert).toBeDefined();
    expect(screen.getByText('Bağlantı Hatası')).toBeDefined();
    expect(screen.getByText(/Sunucu ile iletişim/)).toBeDefined();
  });

  it('triggers onRetry when retry button is clicked', () => {
    const handleRetry = vi.fn();
    render(
      <ErrorState
        title="Hata"
        message="Bir problem oluştu."
        onRetry={handleRetry}
        retryLabel="Yeniden Bağlan"
      />
    );

    const retryBtn = screen.getByRole('button', { name: 'Yeniden Bağlan' });
    fireEvent.click(retryBtn);

    expect(handleRetry).toHaveBeenCalledTimes(1);
  });

  it('does not render retry button if onRetry is omitted', () => {
    render(
      <ErrorState
        title="Hata"
        message="Detay yok."
      />
    );

    expect(screen.queryByRole('button')).toBeNull();
  });
});
