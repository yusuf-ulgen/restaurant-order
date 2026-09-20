import { createRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Checkbox } from '../Checkbox';

describe('Checkbox Component', () => {
  it('renders label and description', () => {
    render(
      <Checkbox
        label="Acılı Sos"
        description="Ekstra ücret uygulanır (+15 TL)"
      />
    );

    expect(screen.getByText('Acılı Sos')).toBeDefined();
    expect(screen.getByText('Ekstra ücret uygulanır (+15 TL)')).toBeDefined();
  });

  it('handles toggle interaction', () => {
    const handleChange = vi.fn();
    render(<Checkbox label="Servis Çağır" onChange={handleChange} />);

    const checkbox = screen.getByRole('checkbox', { name: 'Servis Çağır' });
    fireEvent.click(checkbox);

    expect(handleChange).toHaveBeenCalledTimes(1);
  });

  it('does not toggle when disabled', () => {
    const handleChange = vi.fn();
    render(<Checkbox label="Tükenen Ürün" disabled onChange={handleChange} />);

    const checkbox = screen.getByRole('checkbox', { name: 'Tükenen Ürün' });
    expect(checkbox.hasAttribute('disabled')).toBe(true);

    fireEvent.click(checkbox);
    expect(handleChange).not.toHaveBeenCalled();
  });

  it('sets aria-invalid when isInvalid is true', () => {
    render(<Checkbox label="Zorunlu Seçim" isInvalid />);
    const checkbox = screen.getByRole('checkbox', { name: 'Zorunlu Seçim' });
    expect(checkbox.getAttribute('aria-invalid')).toBe('true');
  });

  it('forwards ref to HTMLInputElement', () => {
    const ref = createRef<HTMLInputElement>();
    render(<Checkbox ref={ref} label="Ref Checkbox" />);
    expect(ref.current).toBeInstanceOf(HTMLInputElement);
  });
});
