import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FormField } from '../FormField';
import { Input } from '../Input';

describe('FormField Component', () => {
  it('connects label with input via htmlFor and id', () => {
    render(
      <FormField id="table-number" label="Masa Numarası">
        <Input placeholder="04" />
      </FormField>
    );

    const label = screen.getByText('Masa Numarası').closest('label');
    const input = screen.getByPlaceholderText('04');

    expect(label?.getAttribute('for')).toBe('table-number');
    expect(input.getAttribute('id')).toBe('table-number');
  });

  it('renders required indicator and sets required attributes', () => {
    render(
      <FormField id="staff-pin" label="Personel PIN" required>
        <Input type="password" />
      </FormField>
    );

    expect(screen.getByText('*')).toBeDefined();
    const input = screen.getByLabelText(/Personel PIN/);
    expect(input.hasAttribute('required')).toBe(true);
    expect(input.getAttribute('aria-required')).toBe('true');
  });

  it('connects description via aria-describedby', () => {
    render(
      <FormField
        id="notes"
        label="Sipariş Notu"
        description="Mutfak şefine iletilecek özel istekler."
      >
        <Input />
      </FormField>
    );

    const input = screen.getByLabelText('Sipariş Notu');
    expect(input.getAttribute('aria-describedby')).toBe('notes-desc');
    expect(screen.getByText('Mutfak şefine iletilecek özel istekler.').id).toBe('notes-desc');
  });

  it('renders error message, sets aria-invalid, and links error via aria-describedby', () => {
    render(
      <FormField
        id="quantity"
        label="Adet"
        description="Varsayılan 1"
        error="Adet 0'dan büyük olmalıdır."
      >
        <Input />
      </FormField>
    );

    const input = screen.getByLabelText('Adet');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toContain('quantity-error');
    expect(screen.getByRole('alert').textContent).toBe("Adet 0'dan büyük olmalıdır.");
  });
});
