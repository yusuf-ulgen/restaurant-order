import { createRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Select } from '../Select';

describe('Select Component', () => {
  const options = [
    { value: 'hall', label: 'Ana Salon' },
    { value: 'terrace', label: 'Teras' },
    { value: 'garden', label: 'Bahçe', disabled: true },
  ];

  it('renders options array correctly', () => {
    render(<Select options={options} aria-label="Bölge Seçimi" defaultValue="terrace" />);
    const select = screen.getByRole('combobox', { name: 'Bölge Seçimi' }) as HTMLSelectElement;

    expect(select.value).toBe('terrace');
    expect(screen.getByText('Ana Salon')).toBeDefined();
    expect(screen.getByText('Bahçe')).toBeDefined();
  });

  it('handles option change events', () => {
    const handleChange = vi.fn();
    render(<Select options={options} aria-label="Bölge Seçimi" onChange={handleChange} />);

    const select = screen.getByRole('combobox', { name: 'Bölge Seçimi' });
    fireEvent.change(select, { target: { value: 'hall' } });

    expect(handleChange).toHaveBeenCalledTimes(1);
  });

  it('is disabled when disabled prop is set', () => {
    render(<Select options={options} aria-label="Kilitli Seçim" disabled />);
    const select = screen.getByRole('combobox', { name: 'Kilitli Seçim' });
    expect(select.hasAttribute('disabled')).toBe(true);
  });

  it('sets aria-invalid when isInvalid is true', () => {
    render(<Select options={options} aria-label="Hatalı Seçim" isInvalid />);
    const select = screen.getByRole('combobox', { name: 'Hatalı Seçim' });
    expect(select.getAttribute('aria-invalid')).toBe('true');
  });

  it('forwards ref to HTMLSelectElement', () => {
    const ref = createRef<HTMLSelectElement>();
    render(<Select ref={ref} options={options} aria-label="Ref Test" />);
    expect(ref.current).toBeInstanceOf(HTMLSelectElement);
  });
});
