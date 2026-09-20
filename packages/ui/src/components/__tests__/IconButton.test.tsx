import { createRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { IconButton } from '../IconButton';

describe('IconButton Component', () => {
  it('renders with mandatory accessible name', () => {
    render(<IconButton aria-label="Menüyü Kapat" icon={<span>✕</span>} />);
    expect(screen.getByRole('button', { name: 'Menüyü Kapat' })).toBeDefined();
  });

  it('triggers onClick when clicked', () => {
    const handleClick = vi.fn();
    render(<IconButton aria-label="Sepeti Aç" icon={<span>🛒</span>} onClick={handleClick} />);

    fireEvent.click(screen.getByRole('button', { name: 'Sepeti Aç' }));
    expect(handleClick).toHaveBeenCalledTimes(1);
  });

  it('does not trigger onClick when disabled', () => {
    const handleClick = vi.fn();
    render(<IconButton aria-label="Engelli Buton" icon={<span>🔒</span>} disabled onClick={handleClick} />);

    const button = screen.getByRole('button', { name: 'Engelli Buton' });
    expect(button.hasAttribute('disabled')).toBe(true);

    fireEvent.click(button);
    expect(handleClick).not.toHaveBeenCalled();
  });

  it('displays spinner and sets aria-busy when loading', () => {
    const handleClick = vi.fn();
    render(<IconButton aria-label="Yükleniyor" icon={<span>⚙</span>} loading onClick={handleClick} />);

    const button = screen.getByRole('button', { name: 'Yükleniyor' });
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.hasAttribute('disabled')).toBe(true);

    fireEvent.click(button);
    expect(handleClick).not.toHaveBeenCalled();
  });

  it('forwards ref to button element', () => {
    const ref = createRef<HTMLButtonElement>();
    render(<IconButton ref={ref} aria-label="Ref Test" icon={<span>✓</span>} />);
    expect(ref.current).toBeInstanceOf(HTMLButtonElement);
  });
});
