import { createRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Input } from '../Input';

describe('Input Component', () => {
  it('renders input with placeholder and value', () => {
    render(<Input placeholder="Masa numarası girin" defaultValue="12" />);
    const input = screen.getByPlaceholderText('Masa numarası girin') as HTMLInputElement;
    expect(input.value).toBe('12');
  });

  it('handles user input change events', () => {
    const handleChange = vi.fn();
    render(<Input placeholder="Masa ara" onChange={handleChange} />);

    const input = screen.getByPlaceholderText('Masa ara');
    fireEvent.change(input, { target: { value: 'Salon 4' } });

    expect(handleChange).toHaveBeenCalledTimes(1);
  });

  it('is disabled when disabled prop is set', () => {
    render(<Input placeholder="Kilitli Alan" disabled />);
    const input = screen.getByPlaceholderText('Kilitli Alan');
    expect(input.hasAttribute('disabled')).toBe(true);
  });

  it('sets aria-invalid and error border when isInvalid is true', () => {
    const { container } = render(<Input placeholder="Hatalı Alan" isInvalid />);
    const input = container.querySelector('input');

    expect(input?.getAttribute('aria-invalid')).toBe('true');
    expect(input?.style.border).toContain('var(--ro-color-error)');
  });

  it('forwards ref to HTMLInputElement', () => {
    const ref = createRef<HTMLInputElement>();
    render(<Input ref={ref} placeholder="Ref Input" />);
    expect(ref.current).toBeInstanceOf(HTMLInputElement);
  });
});
