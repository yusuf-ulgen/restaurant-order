import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Divider } from '../Divider';

describe('Divider Component', () => {
  it('renders horizontal separator by default', () => {
    render(<Divider />);
    const separator = screen.getByRole('separator');

    expect(separator.getAttribute('aria-orientation')).toBe('horizontal');
    expect(separator.style.width).toBe('100%');
  });

  it('renders vertical separator when orientation is vertical', () => {
    render(<Divider orientation="vertical" />);
    const separator = screen.getByRole('separator');

    expect(separator.getAttribute('aria-orientation')).toBe('vertical');
    expect(separator.style.width).toBe('1px');
  });
});
