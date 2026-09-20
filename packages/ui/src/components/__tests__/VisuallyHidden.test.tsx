import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { VisuallyHidden } from '../VisuallyHidden';

describe('VisuallyHidden Component', () => {
  it('renders content with screen-reader-only styles', () => {
    render(<VisuallyHidden>Masa durumunu gizli oku</VisuallyHidden>);
    const element = screen.getByText('Masa durumunu gizli oku');

    expect(element.style.position).toBe('absolute');
    expect(element.style.clip).toBe('rect(0px, 0px, 0px, 0px)');
    expect(element.style.overflow).toBe('hidden');
  });

  it('supports rendering as custom element', () => {
    const { container } = render(
      <VisuallyHidden as="div">
        <span>Özet Bilgi</span>
      </VisuallyHidden>
    );

    expect(container.querySelector('div')).toBeDefined();
    expect(screen.getByText('Özet Bilgi')).toBeDefined();
  });
});
