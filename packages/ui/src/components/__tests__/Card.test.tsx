import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Card } from '../Card';

describe('Card Component', () => {
  it('renders content inside the card', () => {
    render(<Card>Card Content Body</Card>);
    expect(screen.getByText('Card Content Body')).toBeDefined();
  });

  it('applies padding styles accurately', () => {
    const { container } = render(<Card padding="lg">Large Card</Card>);
    const cardDiv = container.firstChild as HTMLElement;
    expect(cardDiv.style.padding).toBe('32px');
  });
});
