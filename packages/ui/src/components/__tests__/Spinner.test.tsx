import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Spinner } from '../Spinner';

describe('Spinner Component', () => {
  it('renders with status role and default label', () => {
    render(<Spinner />);
    const spinner = screen.getByRole('status');

    expect(spinner.getAttribute('aria-label')).toBe('Yükleniyor...');
  });

  it('renders with custom label and dimensions', () => {
    render(<Spinner size="lg" label="Sipariş iletiliyor..." />);
    const spinner = screen.getByRole('status');

    expect(spinner.getAttribute('aria-label')).toBe('Sipariş iletiliyor...');
    expect(spinner.style.width).toBe('32px');
    expect(spinner.style.height).toBe('32px');
  });
});
