import { createRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Switch } from '../Switch';

describe('Switch Component', () => {
  it('renders with role="switch" and correct aria-checked', () => {
    render(<Switch checked={true} label="Mutfak Bildirimleri" />);
    const toggle = screen.getByRole('switch');

    expect(toggle.getAttribute('aria-checked')).toBe('true');
    expect(screen.getByText('Mutfak Bildirimleri')).toBeDefined();
  });

  it('triggers onCheckedChange on click', () => {
    const handleCheck = vi.fn();
    render(<Switch checked={false} onCheckedChange={handleCheck} label="Sesli Uyarı" />);

    const toggle = screen.getByRole('switch');
    fireEvent.click(toggle);

    expect(handleCheck).toHaveBeenCalledWith(true);
  });

  it('toggles on Enter and Space key presses', () => {
    const handleCheck = vi.fn();
    render(<Switch checked={false} onCheckedChange={handleCheck} label="Klavye Test" />);

    const toggle = screen.getByRole('switch');

    fireEvent.keyDown(toggle, { key: ' ' });
    expect(handleCheck).toHaveBeenCalledWith(true);

    fireEvent.keyDown(toggle, { key: 'Enter' });
    expect(handleCheck).toHaveBeenCalledTimes(2);
  });

  it('does not toggle when disabled', () => {
    const handleCheck = vi.fn();
    render(<Switch checked={false} disabled onCheckedChange={handleCheck} label="Kapalı Switch" />);

    const toggle = screen.getByRole('switch');
    expect(toggle.hasAttribute('disabled')).toBe(true);

    fireEvent.click(toggle);
    expect(handleCheck).not.toHaveBeenCalled();

    fireEvent.keyDown(toggle, { key: ' ' });
    expect(handleCheck).not.toHaveBeenCalled();
  });

  it('forwards ref to HTMLButtonElement', () => {
    const ref = createRef<HTMLButtonElement>();
    render(<Switch ref={ref} checked={false} label="Ref Switch" />);
    expect(ref.current).toBeInstanceOf(HTMLButtonElement);
  });
});
