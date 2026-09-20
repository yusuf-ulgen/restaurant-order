import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { Skeleton } from '../Skeleton';

describe('Skeleton Component', () => {
  it('renders with aria-hidden="true"', () => {
    const { container } = render(<Skeleton />);
    const skeleton = container.firstChild as HTMLElement;

    expect(skeleton.getAttribute('aria-hidden')).toBe('true');
    expect(skeleton.style.borderRadius).toBe('var(--ro-radius-xs)');
  });

  it('supports circular variant with full radius', () => {
    const { container } = render(<Skeleton variant="circular" width={48} height={48} />);
    const skeleton = container.firstChild as HTMLElement;

    expect(skeleton.style.borderRadius).toBe('var(--ro-radius-full)');
    expect(skeleton.style.width).toBe('48px');
    expect(skeleton.style.height).toBe('48px');
  });

  it('supports rectangular variant with md radius', () => {
    const { container } = render(<Skeleton variant="rectangular" height={160} />);
    const skeleton = container.firstChild as HTMLElement;

    expect(skeleton.style.borderRadius).toBe('var(--ro-radius-md)');
    expect(skeleton.style.height).toBe('160px');
  });
});
