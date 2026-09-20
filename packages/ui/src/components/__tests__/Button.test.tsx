import { createRef } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Button } from '../Button';

describe('Button Component', () => {
  it('renders children correctly', () => {
    render(<Button>Click Me</Button>);
    expect(screen.getByText('Click Me')).toBeDefined();
  });

  it('triggers onClick when clicked', () => {
    const handleClick = vi.fn();
    render(<Button onClick={handleClick}>Submit</Button>);

    fireEvent.click(screen.getByText('Submit'));
    expect(handleClick).toHaveBeenCalledTimes(1);
  });

  it('does not trigger onClick when disabled', () => {
    const handleClick = vi.fn();
    render(<Button disabled onClick={handleClick}>Disabled Button</Button>);

    fireEvent.click(screen.getByText('Disabled Button'));
    expect(handleClick).not.toHaveBeenCalled();
  });

  it('applies variant styling', () => {
    const { container } = render(<Button variant="danger">Delete</Button>);
    const button = container.querySelector('button');
    expect(button?.style.backgroundColor).toBe('var(--ro-color-danger)');
  });

  it('supports ghost variant', () => {
    const { container } = render(<Button variant="ghost">Ghost Action</Button>);
    const button = container.querySelector('button');
    expect(button?.style.backgroundColor).toBe('transparent');
  });

  it('displays spinner and prevents click when loading', () => {
    const handleClick = vi.fn();
    render(<Button loading onClick={handleClick}>Loading Action</Button>);

    const button = screen.getByRole('button');
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.hasAttribute('disabled')).toBe(true);

    fireEvent.click(button);
    expect(handleClick).not.toHaveBeenCalled();
  });

  it('forwards ref to HTMLButtonElement', () => {
    const ref = createRef<HTMLButtonElement>();
    render(<Button ref={ref}>Ref Button</Button>);
    expect(ref.current).toBeInstanceOf(HTMLButtonElement);
  });
});
