import React from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { ToastProvider } from '../ToastContext';
import { useToast } from '../useToast';

const TestToastConsumer: React.FC = () => {
  const toast = useToast();

  return (
    <div>
      <button
        data-testid="btn-success"
        onClick={() => toast.success('İşlem başarılı', { title: 'Başarılı' })}
      >
        Success
      </button>
      <button
        data-testid="btn-error"
        onClick={() => toast.error('Hata oluştu', { title: 'Hata' })}
      >
        Error
      </button>
      <button
        data-testid="btn-warning"
        onClick={() => toast.warning('Dikkat ediniz')}
      >
        Warning
      </button>
      <button
        data-testid="btn-info"
        onClick={() => toast.info('Bilgilendirme')}
      >
        Info
      </button>
      <button data-testid="btn-clear" onClick={() => toast.clear()}>
        Clear
      </button>
    </div>
  );
};

describe('Toast System', () => {
  it('throws error when useToast is used outside ToastProvider', () => {
    // Suppress console.error for this test
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<TestToastConsumer />)).toThrow(
      'useToast must be used within a ToastProvider'
    );
    consoleSpy.mockRestore();
  });

  it('renders success toast with role="status"', () => {
    render(
      <ToastProvider>
        <TestToastConsumer />
      </ToastProvider>
    );

    fireEvent.click(screen.getByTestId('btn-success'));

    const toast = screen.getByRole('status');
    expect(toast).toBeDefined();
    expect(screen.getByText('Başarılı')).toBeDefined();
    expect(screen.getByText('İşlem başarılı')).toBeDefined();
  });

  it('renders error and warning toasts with role="alert"', () => {
    render(
      <ToastProvider>
        <TestToastConsumer />
      </ToastProvider>
    );

    fireEvent.click(screen.getByTestId('btn-error'));
    fireEvent.click(screen.getByTestId('btn-warning'));

    const alerts = screen.getAllByRole('alert');
    expect(alerts.length).toBe(2);
    expect(screen.getByText('Hata oluştu')).toBeDefined();
    expect(screen.getByText('Dikkat ediniz')).toBeDefined();
  });

  it('removes toast when close button is clicked', () => {
    render(
      <ToastProvider>
        <TestToastConsumer />
      </ToastProvider>
    );

    fireEvent.click(screen.getByTestId('btn-info'));
    expect(screen.getByText('Bilgilendirme')).toBeDefined();

    const closeBtn = screen.getByRole('button', { name: 'Kapat' });
    fireEvent.click(closeBtn);

    expect(screen.queryByText('Bilgilendirme')).toBeNull();
  });

  it('auto-dismisses toast after duration', () => {
    vi.useFakeTimers();
    render(
      <ToastProvider>
        <TestToastConsumer />
      </ToastProvider>
    );

    fireEvent.click(screen.getByTestId('btn-info'));
    expect(screen.getByText('Bilgilendirme')).toBeDefined();

    act(() => {
      vi.advanceTimersByTime(4500);
    });

    expect(screen.queryByText('Bilgilendirme')).toBeNull();
    vi.useRealTimers();
  });

  it('clears all toasts when clear is called', () => {
    render(
      <ToastProvider>
        <TestToastConsumer />
      </ToastProvider>
    );

    fireEvent.click(screen.getByTestId('btn-success'));
    fireEvent.click(screen.getByTestId('btn-error'));

    expect(screen.getByText('İşlem başarılı')).toBeDefined();
    expect(screen.getByText('Hata oluştu')).toBeDefined();

    fireEvent.click(screen.getByTestId('btn-clear'));

    expect(screen.queryByText('İşlem başarılı')).toBeNull();
    expect(screen.queryByText('Hata oluştu')).toBeNull();
  });

  it('respects maxToasts limit', () => {
    render(
      <ToastProvider maxToasts={2}>
        <TestToastConsumer />
      </ToastProvider>
    );

    fireEvent.click(screen.getByTestId('btn-success'));
    fireEvent.click(screen.getByTestId('btn-error'));
    fireEvent.click(screen.getByTestId('btn-warning'));

    // Only the last 2 should remain
    expect(screen.queryByText('İşlem başarılı')).toBeNull();
    expect(screen.getByText('Hata oluştu')).toBeDefined();
    expect(screen.getByText('Dikkat ediniz')).toBeDefined();
  });
});
