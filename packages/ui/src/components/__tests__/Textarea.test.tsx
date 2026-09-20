import { createRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Textarea } from '../Textarea';

describe('Textarea Component', () => {
  it('renders textarea with default rows and placeholder', () => {
    render(<Textarea placeholder="Sipariş notu ekleyin" rows={4} defaultValue="Az acılı olsun" />);
    const textarea = screen.getByPlaceholderText('Sipariş notu ekleyin') as HTMLTextAreaElement;
    expect(textarea.value).toBe('Az acılı olsun');
    expect(textarea.rows).toBe(4);
  });

  it('handles user text input change events', () => {
    const handleChange = vi.fn();
    render(<Textarea placeholder="Notlar" onChange={handleChange} />);

    const textarea = screen.getByPlaceholderText('Notlar');
    fireEvent.change(textarea, { target: { value: 'Buzsuz lütfen' } });

    expect(handleChange).toHaveBeenCalledTimes(1);
  });

  it('is disabled when disabled prop is set', () => {
    render(<Textarea placeholder="Kapalı" disabled />);
    const textarea = screen.getByPlaceholderText('Kapalı');
    expect(textarea.hasAttribute('disabled')).toBe(true);
  });

  it('sets aria-invalid and error border when isInvalid is true', () => {
    const { container } = render(<Textarea placeholder="Hata" isInvalid />);
    const textarea = container.querySelector('textarea');

    expect(textarea?.getAttribute('aria-invalid')).toBe('true');
    expect(textarea?.style.border).toContain('var(--ro-color-error)');
  });

  it('forwards ref to HTMLTextAreaElement', () => {
    const ref = createRef<HTMLTextAreaElement>();
    render(<Textarea ref={ref} placeholder="Ref Textarea" />);
    expect(ref.current).toBeInstanceOf(HTMLTextAreaElement);
  });
});
