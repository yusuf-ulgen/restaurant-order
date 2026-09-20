import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AppHeader } from '../AppHeader';
import { AppFooter } from '../AppFooter';

describe('AppHeader Component', () => {
  it('renders title and subtitle', () => {
    render(<AppHeader title="Restoran Sipariş" subtitle="Masa 04" />);

    expect(screen.getByTestId('header-title').textContent).toBe('Restoran Sipariş');
    expect(screen.getByTestId('header-subtitle').textContent).toBe('Masa 04');
  });

  it('renders logo component and logoUrl', () => {
    const { rerender } = render(
      <AppHeader title="Test" logo={<span data-testid="custom-logo">LOGO</span>} />
    );
    expect(screen.getByTestId('custom-logo')).toBeDefined();

    rerender(<AppHeader title="Test" logoUrl="/assets/logo.png" />);
    const img = screen.getByRole('img');
    expect(img.getAttribute('src')).toBe('/assets/logo.png');
  });

  it('renders actions area', () => {
    render(
      <AppHeader
        title="Test"
        actions={<button data-testid="action-btn">Çıkış</button>}
      />
    );
    expect(screen.getByTestId('action-btn')).toBeDefined();
  });

  it('renders mobile menu toggle button and triggers onMobileMenuToggle', () => {
    const handleToggle = vi.fn();
    render(
      <AppHeader
        title="Test"
        showMobileMenuToggle={true}
        onMobileMenuToggle={handleToggle}
        isMobileMenuOpen={false}
      />
    );

    const toggleBtn = screen.getByTestId('mobile-menu-toggle');
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('false');

    fireEvent.click(toggleBtn);
    expect(handleToggle).toHaveBeenCalledTimes(1);
  });
});

describe('AppFooter Component', () => {
  it('renders copyright and business information', () => {
    render(
      <AppFooter
        copyright="© 2026 Restaurant Order"
        businessText="Kadıköy Şubesi • Tel: 0216 123 45 67"
      />
    );

    expect(screen.getByTestId('footer-copyright').textContent).toBe(
      '© 2026 Restaurant Order'
    );
    expect(screen.getByTestId('footer-business').textContent).toBe(
      'Kadıköy Şubesi • Tel: 0216 123 45 67'
    );
  });

  it('renders safe navigation links with external attributes', () => {
    const handleLinkClick = vi.fn();
    render(
      <AppFooter
        links={[
          { id: '1', label: 'Gizlilik Politikası', onClick: handleLinkClick },
          { id: '2', label: 'Yardım', href: 'https://example.com', isExternal: true },
        ]}
      />
    );

    const internalLink = screen.getByText('Gizlilik Politikası');
    fireEvent.click(internalLink);
    expect(handleLinkClick).toHaveBeenCalledTimes(1);

    const externalLink = screen.getByText('Yardım');
    expect(externalLink.getAttribute('target')).toBe('_blank');
    expect(externalLink.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('does not render when visible is false', () => {
    render(<AppFooter visible={false} copyright="Hidden Copyright" />);
    expect(screen.queryByTestId('app-footer')).toBeNull();
  });
});
