import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Badge } from '../Badge';

describe('Badge Component', () => {
  it('renders badge label correctly', () => {
    render(<Badge>Active</Badge>);
    expect(screen.getByText('Active')).toBeDefined();
  });

  it('applies variant colors for status indicators', () => {
    const { container: successContainer } = render(<Badge variant="success">Completed</Badge>);
    const successBadge = successContainer.querySelector('span');
    expect(successBadge?.style.color).toBe('var(--ro-color-success)');

    const { container: warningContainer } = render(<Badge variant="warning">Pending</Badge>);
    const warningBadge = warningContainer.querySelector('span');
    expect(warningBadge?.style.color).toBe('var(--ro-color-warning)');
  });
});
